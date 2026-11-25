using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace BlotterSync.Agent;

public sealed class ChatHarness(AgentStore store, AgentTools tools, AgentModel model, ILogger<ChatHarness> logger)
{
    public const int MaxTurns = 40;

    public async Task<ChatView?> SendAsync(ChatRequest request, int officerId, bool isAdmin, CancellationToken ct)
    {
        var question = request.Message.Trim();
        if (question.Length == 0) throw new AgentFailure("Enter a message before sending.");
        var isNew = request.ThreadId is null;
        var thread = isNew
            ? new ChatThread { OfficerId = officerId, RecordId = request.RecordId, Title = MakeTitle(question) }
            : await store.ChatThreads.SingleOrDefaultAsync(t => t.Id == request.ThreadId && t.OfficerId == officerId, ct);
        if (thread is null) return null;
        if (!isNew && request.RecordId is not null)
            throw new AgentFailure("Start a new chat to change the incident.");
        if (!isNew && request.Version != thread.Version) throw new ChatConflict();
        if (thread.Version >= MaxTurns) throw new AgentFailure("This chat is full. Start a new chat to continue.");

        // Recheck current access before sending prior messages to the provider or serving a reply.
        if (!await CanAccessAsync(thread.RecordId, officerId, isAdmin, ct)) return null;
        var turns = ReadTurns(thread);
        var steps = new List<AgentStep>();
        var mode = model.Mode;
        var usesModel = mode != "local";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var entered = false;
        ChatAnswer answer;
        try
        {
            entered = await AgentHarness.Slots.WaitAsync(TimeSpan.FromSeconds(2), timeout.Token);
            if (!entered) throw new AgentFailure("The assistant is busy. Try again in a moment.");
            answer = usesModel
                ? await model.ChatAsync(question, turns, tools, officerId, isAdmin, thread.RecordId, steps, timeout.Token)
                : await LocalReplyAsync(question, thread.RecordId, officerId, isAdmin, steps, timeout.Token);
            ValidateAnswer(answer, thread.RecordId, steps);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AgentFailure("The chat request timed out. Try sending a shorter question.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or HttpRequestException or KeyNotFoundException)
        {
            logger.LogWarning("Chat request failed with {ErrorType}.", ex.GetType().Name);
            throw new AgentFailure("The assistant could not return a valid reply. Try again.");
        }
        finally
        {
            if (entered) AgentHarness.Slots.Release();
        }

        steps.Add(new("compose_reply", usesModel ? $"Prepared a reply with {model.Model}." : "Prepared a local reply.", DateTime.UtcNow));
        thread.UpdatedAt = DateTime.UtcNow;
        turns.Add(new(question, answer.Reply.Trim(), mode, answer.SourceRecordIds, steps, thread.UpdatedAt));
        thread.TurnsJson = JsonSerializer.Serialize(turns, AgentJson.Options);
        thread.Version++;
        if (isNew) store.ChatThreads.Add(thread);
        try { await store.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ChatConflict(); }
        return new(thread.Id, thread.RecordId, thread.Title, thread.Version, turns);
    }

    public async Task<ChatView?> GetAsync(Guid id, int officerId, bool isAdmin, CancellationToken ct)
    {
        var thread = await store.ChatThreads.AsNoTracking().SingleOrDefaultAsync(
            t => t.Id == id && t.OfficerId == officerId, ct);
        if (thread is null || !await CanAccessAsync(thread.RecordId, officerId, isAdmin, ct)) return null;
        return new(thread.Id, thread.RecordId, thread.Title, thread.Version, ReadTurns(thread));
    }

    public async Task<IReadOnlyList<ChatThreadSummary>> RecentAsync(int officerId, bool isAdmin, CancellationToken ct)
    {
        var recent = await store.ChatThreads.AsNoTracking().Where(t => t.OfficerId == officerId)
            .OrderByDescending(t => t.UpdatedAt).Take(30)
            .Select(t => new ChatThreadSummary(t.Id, t.RecordId, t.Title, t.UpdatedAt)).ToListAsync(ct);
        var allowed = await tools.AccessibleRecordIdsAsync(recent.Where(t => t.RecordId.HasValue)
            .Select(t => t.RecordId!.Value).Distinct().ToArray(), officerId, isAdmin, ct);
        return recent.Where(t => t.RecordId is null || allowed.Contains(t.RecordId.Value)).ToArray();
    }

    private async Task<bool> CanAccessAsync(int? id, int officerId, bool isAdmin, CancellationToken ct)
        => id is null || (await tools.AccessibleRecordIdsAsync([id.Value], officerId, isAdmin, ct)).Length > 0;

    private static List<ChatTurn> ReadTurns(ChatThread thread)
        => JsonSerializer.Deserialize<List<ChatTurn>>(thread.TurnsJson, AgentJson.Options) ?? [];

    private static string MakeTitle(string question)
    {
        var title = Regex.Replace(question, @"\s+", " ");
        return title.Length <= 72 ? title : title[..71] + "…";
    }

    private static void ValidateAnswer(ChatAnswer answer, int? recordId, List<AgentStep> steps)
    {
        if (string.IsNullOrWhiteSpace(answer.Reply) || answer.Reply.Length > 6000 || answer.SourceRecordIds is null)
            throw new AgentFailure("The assistant returned an invalid reply.");
        var readIncident = steps.Any(step => step.Tool == "read_incident");
        if (readIncident
            ? answer.SourceRecordIds.Length != 1 || answer.SourceRecordIds[0] != recordId
            : answer.SourceRecordIds.Length != 0)
            throw new AgentFailure("The assistant returned an unverified source reference. Try asking again.");
    }

    private async Task<ChatAnswer> LocalReplyAsync(string question, int? id, int officerId,
        bool isAdmin, List<AgentStep> steps, CancellationToken ct)
    {
        bool Has(string words) => Regex.IsMatch(question, $@"\b(?:{words})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (Has("categories|category list|list.*category"))
        {
            var result = await tools.ExecuteAsync("list_categories", "{}", officerId, isAdmin, id, steps, ct);
            var categories = JsonSerializer.Deserialize<string[]>(result, AgentJson.Options) ?? [];
            return new(categories.Length == 0 ? "The category directory is empty."
                : "Incident categories\n" + string.Join("\n", categories.Select(name => $"• {name}")), []);
        }
        var intake = Has("intake|new report|file a report");
        var verification = Has("follow.up|next steps|verify|verification|missing details|checklist");
        if (intake || (verification && id is null))
            return new("Confirm who reported the incident, when and where it happened, what each person directly observed, and any witnesses or supporting material. Mark uncertain details as reported. Open Intake assistant to prepare a filing draft for review.", []);

        var status = Has("status|pending|resolved");
        var location = Has("location|where");
        var date = Has("when|date|time");
        var category = Has("category|classification");
        var brief = Has("summary|summarize|brief|narrative|details|reported|show incident|record");
        if (status || location || date || category || brief || verification)
        {
            if (id is null) return new("Choose an incident above the chat, then send your question again.", []);
            var result = await tools.ExecuteAsync("read_incident", JsonSerializer.Serialize(new { recordId = id }),
                officerId, isAdmin, id, steps, ct);
            var source = JsonSerializer.Deserialize<IncidentSource>(result, AgentJson.Options)!;
            if (verification)
                return new($"{source.TrackingNumber} is marked {source.Status}. Verify the account with the reporting party, confirm the date and location, and ask whether witness statements or supporting material are available.", [id.Value]);
            var facts = new List<string>();
            if (status || brief) facts.Add($"Status: {source.Status}");
            if (location || brief) facts.Add($"Location: {source.Location}");
            if (date || brief) facts.Add($"Incident time: {source.IncidentDate.AddHours(8).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} (Philippine time)");
            if (category || brief) facts.Add($"Category: {source.Category}");
            if (brief)
            {
                var excerpt = source.Narrative.Length > 3000 ? source.Narrative[..3000] + "…" : source.Narrative;
                facts.Add($"Recorded narrative\n{excerpt}");
            }
            return new(source.TrackingNumber + "\n\n" + string.Join("\n", facts), [id.Value]);
        }
        return new("Local chat supports incident details, status, location, category, intake checklists, and the category directory. Try “List incident categories” or attach an incident and ask “What is the status?” Connect a model for open-ended questions and conversational follow-ups.", []);
    }
}
