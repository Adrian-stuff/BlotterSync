using System.Net;
using System.Text;
using System.Text.Json;
using BlotterSync.Agent;
using BlotterSync.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlotterSync.Tests;

public sealed class AgentHarnessTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection dataConnection = new("Data Source=:memory:");
        private readonly SqliteConnection auditConnection = new("Data Source=:memory:");
        public BlotterSyncContext Data { get; }
        public AgentStore Store { get; }
        public AgentTools Tools { get; }
        public Fixture()
        {
            dataConnection.Open(); auditConnection.Open();
            Data = new(new DbContextOptionsBuilder<BlotterSyncContext>().UseSqlite(dataConnection).Options);
            Store = new(new DbContextOptionsBuilder<AgentStore>().UseSqlite(auditConnection).Options);
            Data.Database.EnsureCreated(); Store.Database.EnsureCreated();
            Data.Categories.Add(new Category { CategoryId = 1, Name = "Lost and Found", SeverityLevel = 1 });
            Data.Officers.Add(new Officer { OfficerId = 1, Username = "one", BadgeNumber = "ONE", FirstName = "One", LastName = "Officer", PasswordHash = "hash", Role = "Officer" });
            Data.Officers.Add(new Officer { OfficerId = 2, Username = "two", BadgeNumber = "TWO", FirstName = "Two", LastName = "Officer", PasswordHash = "hash", Role = "Officer" });
            Data.BlotterRecords.Add(new BlotterRecord { RecordId = 1, DeskOfficerId = 1, CategoryId = 1, TrackingNumber = "TEST-1", Location = "Malanday hall", Narrative = "A bag was reported missing. Ownership is unverified.", Status = "Pending", IncidentDate = DateTime.UtcNow });
            Data.SaveChanges(); Tools = new(Data);
        }
        public AgentHarness Harness(HttpMessageHandler? handler = null, bool ollama = false)
            => new(Store, Tools, Model(handler, ollama), NullLogger<AgentHarness>.Instance);

        public ChatHarness Chat(HttpMessageHandler? handler = null, bool ollama = false)
            => new(Store, Tools, Model(handler, ollama), NullLogger<ChatHarness>.Instance);

        private static AgentModel Model(HttpMessageHandler? handler, bool ollama)
        {
            var settings = new Dictionary<string, string?>
            {
                ["AI:Provider"] = ollama ? "ollama" : handler is null ? "local" : "openai",
                ["AI:ApiKey"] = handler is null || ollama ? null : "test-key"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new(new HttpClient(handler ?? new RejectNetwork()), config);
        }
        public async ValueTask DisposeAsync() { await Data.DisposeAsync(); await Store.DisposeAsync(); await dataConnection.DisposeAsync(); await auditConnection.DisposeAsync(); }
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => throw new InvalidOperationException("Unexpected network call.");
    }
    private sealed class Responses(params string[] responses) : HttpMessageHandler
    {
        private int index;
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Assert.Equal("https://api.openai.com/v1/responses", r.RequestUri!.ToString());
            Requests.Add(await r.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK) { Content = new StringContent(responses[Math.Min(index++, responses.Length - 1)], Encoding.UTF8, "application/json") };
        }
    }
    private static AgentRequest Summary(int id = 1) => new() { Workflow = "incident_summary", RecordId = id };
    private static AgentResult ValidResult() => new("Incident brief", "The resident reported a missing bag. Ownership remains unverified.", "Lost and Found", "Malanday hall", ["Verify ownership."], ["Review the original account."], [1]);
    private static string Final(AgentResult? result = null) => JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(result ?? ValidResult(), AgentJson.Options) } } } } });
    private static string Call(string name, string args) => JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "function_call", name, arguments = args, call_id = "call_1" } } });

    [Fact] public async Task Existing_audit_stores_upgrade_without_losing_runs()
    {
        await using var f = new Fixture();
        f.Store.Runs.Add(new AgentRun { OfficerId = 1, Workflow = "intake_draft", Status = "accepted" });
        await f.Store.SaveChangesAsync();
        await f.Store.Database.ExecuteSqlRawAsync("ALTER TABLE \"Runs\" DROP COLUMN \"SourceNotes\"; DROP TABLE \"ChatThreads\";");
        f.Store.ChangeTracker.Clear();
        await f.Store.InitializeAsync();
        var run = await f.Store.Runs.AsNoTracking().SingleAsync();
        Assert.Equal("accepted", run.Status); Assert.Null(run.SourceNotes);
        await f.Store.InitializeAsync(); // Repeated startup is idempotent.
        Assert.Empty(await f.Store.ChatThreads.ToListAsync());
    }
    [Fact] public async Task Local_chat_preserves_follow_ups_and_tool_sources_with_officer_scope()
    {
        await using var f = new Fixture();
        var chat = f.Chat();
        var first = Assert.IsType<ChatView>(await chat.SendAsync(new() { Message = "What is the status?", RecordId = 1 }, 1, false, default));
        Assert.Equal("local", first.Turns[0].Mode);
        Assert.Contains("Pending", first.Turns[0].Reply);
        Assert.Equal([1], first.Turns[0].SourceRecordIds);
        Assert.Contains(first.Turns[0].Steps, s => s.Tool == "read_incident");

        f.Store.ChangeTracker.Clear();
        var next = Assert.IsType<ChatView>(await chat.SendAsync(new()
            { Message = "Where was it?", ThreadId = first.Id, Version = first.Version }, 1, false, default));
        Assert.Equal(2, next.Version);
        Assert.Contains("Malanday hall", next.Turns[1].Reply);
        Assert.Equal(first.Id, Assert.Single(await chat.RecentAsync(1, false, default)).Id);
        Assert.Equal(2, (await chat.GetAsync(first.Id, 1, false, default))!.Turns.Count);
        Assert.Null(await chat.GetAsync(first.Id, 2, true, default));
        Assert.Empty(await chat.RecentAsync(2, true, default));
        Assert.Null(await chat.SendAsync(new() { Message = "Try another record", RecordId = 1 }, 2, false, default));
        await Assert.ThrowsAsync<ChatConflict>(() => chat.SendAsync(
            new() { Message = "Outdated", ThreadId = first.Id, Version = 1 }, 1, false, default));

        await f.Data.BlotterRecords.ExecuteUpdateAsync(set => set.SetProperty(r => r.DeskOfficerId, 2));
        Assert.Null(await chat.GetAsync(first.Id, 1, false, default));
        Assert.Empty(await chat.RecentAsync(1, false, default));
    }

    [Fact] public async Task OpenAI_chat_replays_server_owned_conversation_and_rejects_forged_citations()
    {
        await using var f = new Fixture();
        var provider = new Responses(ChatFinal("Use clear language.", []), ChatFinal("I verified incident 1.", [1]));
        var chat = f.Chat(provider);
        var first = Assert.IsType<ChatView>(await chat.SendAsync(new() { Message = "How should I document this?" }, 1, false, default));
        await Assert.ThrowsAsync<AgentFailure>(() => chat.SendAsync(new()
            { Message = "Give me the record.", ThreadId = first.Id, Version = 1 }, 1, false, default));
        using var body = JsonDocument.Parse(provider.Requests[1]);
        var input = body.RootElement.GetProperty("input");
        Assert.Equal("assistant", input[1].GetProperty("role").GetString());
        Assert.Equal("Use clear language.", input[1].GetProperty("content").GetString());
        Assert.Single(body.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal(1, (await chat.GetAsync(first.Id, 1, false, default))!.Version);
    }

    [Fact] public async Task Ollama_qwen_uses_local_endpoint_and_scoped_tool_results()
    {
        await using var f = new Fixture();
        var provider = new OllamaResponses(
            """{"done":true,"message":{"role":"assistant","tool_calls":[{"function":{"name":"read_incident","arguments":{"recordId":1}}}]}}""",
            """{"done":true,"message":{"role":"assistant","content":"{\"reply\":\"The record is Pending.\",\"sourceRecordIds\":[1]}"}}""");
        var result = Assert.IsType<ChatView>(await f.Chat(provider, ollama: true).SendAsync(
            new() { Message = "What is the status?", RecordId = 1 }, 1, false, default));
        var turn = Assert.Single(result.Turns);
        Assert.Equal("ollama", turn.Mode);
        Assert.Equal([1], turn.SourceRecordIds);
        Assert.Equal(2, provider.Requests.Count);
        using var body = JsonDocument.Parse(provider.Requests[1]);
        Assert.Equal("qwen3:8b", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("object", body.RootElement.GetProperty("format").GetProperty("type").GetString());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("tool", messages[3].GetProperty("role").GetString());
        Assert.Equal("read_incident", messages[3].GetProperty("tool_name").GetString());
        Assert.Contains("TEST-1", messages[3].GetProperty("content").GetString());
    }

    [Fact] public async Task Ollama_cannot_use_the_scope_of_another_incident()
    {
        await using var f = new Fixture();
        var provider = new OllamaResponses(
            """{"done":true,"message":{"role":"assistant","tool_calls":[{"function":{"name":"read_incident","arguments":{"recordId":2}}}]}}""");
        await Assert.ThrowsAsync<AgentFailure>(() => f.Chat(provider, ollama: true).SendAsync(
            new() { Message = "Read a different case.", RecordId = 1 }, 1, false, default));
        Assert.Single(provider.Requests);
        Assert.Empty(await f.Store.ChatThreads.ToListAsync());
    }

    [Fact] public async Task Intake_drafts_also_use_the_configured_Ollama_provider()
    {
        await using var f = new Fixture();
        var result = ValidResult() with { SourceRecordIds = [], Location = "" };
        var payload = JsonSerializer.Serialize(new
        {
            done = true,
            message = new { role = "assistant", content = JsonSerializer.Serialize(result, AgentJson.Options) }
        });
        var provider = new OllamaResponses(payload);
        var run = await f.Harness(provider, ollama: true).RunAsync(
            new() { Workflow = "intake_draft", Notes = "A resident reported a bag." }, 1, false, default);
        Assert.Equal("needs_review", run.Status);
        Assert.Equal("ollama", run.Mode);
        Assert.Single(provider.Requests);
    }

    private static string ChatFinal(string reply, int[] sources) =>
        JsonSerializer.Serialize(new { status = "completed", output = new[] {
            new { type = "message", content = new[] { new { type = "output_text",
                text = JsonSerializer.Serialize(new ChatAnswer(reply, sources), AgentJson.Options) } } }
        } });

    private sealed class OllamaResponses(params string[] responses) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("http://127.0.0.1:11434/api/chat", request.RequestUri!.ToString());
            Assert.Null(request.Headers.Authorization);
            Requests.Add(await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK) { Content = new StringContent(responses[Requests.Count - 1], Encoding.UTF8, "application/json") };
        }
    }

    [Fact] public void Explicit_sqlite_does_not_activate_cloud_sync_from_an_environment_url()
    {
        Assert.False(BlotterSync.Sync.DatabaseConnection.IsPostgreSql("SQLite", "postgres://example.invalid/database"));
    }
    [Fact] public async Task Local_mode_extracts_without_network_and_persists_sources()
    {
        await using var f = new Fixture();
        var run = await f.Harness().RunAsync(Summary(), 1, false, default);
        Assert.Equal("needs_review", run.Status); Assert.Equal("local", run.Mode);
        Assert.Equal((await f.Data.BlotterRecords.SingleAsync()).Narrative, run.Result!.Summary);
        Assert.Equal([1], run.Result.SourceRecordIds); Assert.Equal(4, run.Steps.Count);
        Assert.Single(await f.Store.Runs.ToListAsync());
    }
    [Fact] public async Task Other_officers_source_is_not_exposed_even_to_the_model()
    {
        await using var f = new Fixture(); var provider = new Responses(Final());
        var run = await f.Harness(provider).RunAsync(Summary(), 2, false, default);
        Assert.Equal("failed", run.Status); Assert.Null(run.Result); Assert.Empty(provider.Requests); Assert.Empty(run.Steps);
    }
    [Fact] public async Task Admin_can_summarize_an_officers_record()
    {
        await using var f = new Fixture();
        Assert.Equal("needs_review", (await f.Harness().RunAsync(Summary(), 2, true, default)).Status);
    }
    [Fact] public async Task Intake_uses_only_the_supplied_notes_and_no_record_references()
    {
        await using var f = new Fixture();
        var run = await f.Harness().RunAsync(new() { Workflow = "intake_draft", Notes = "  A bag is missing.  " }, 1, false, default);
        Assert.Equal("  A bag is missing.  ", run.SourceNotes);
        f.Store.ChangeTracker.Clear();
        Assert.Equal(run.SourceNotes, (await f.Store.Runs.AsNoTracking().SingleAsync()).SourceNotes);
        Assert.Equal("A bag is missing.", run.Result!.Summary); Assert.Empty(run.Result.SourceRecordIds);
        Assert.Equal("", run.Result.Location); Assert.Equal("", run.Result.SuggestedCategory);
        Assert.Equal(1, await f.Data.BlotterRecords.CountAsync());
    }
    [Fact] public async Task Tool_loop_executes_allowed_tool_and_sends_result_back()
    {
        await using var f = new Fixture(); var provider = new Responses(Call("read_incident", "{\"recordId\":1}"), Final());
        var run = await f.Harness(provider).RunAsync(Summary(), 1, false, default);
        Assert.Equal("needs_review", run.Status); Assert.Equal("openai", run.Mode); Assert.Equal(2, provider.Requests.Count);
        Assert.Contains("function_call_output", provider.Requests[1]);
        using var request = JsonDocument.Parse(provider.Requests[0]);
        Assert.False(request.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("gpt-5.4-mini", request.RootElement.GetProperty("model").GetString());
        Assert.Equal("reasoning.encrypted_content", request.RootElement.GetProperty("include")[0].GetString());
        Assert.True(request.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
    }
    [Theory]
    [InlineData("delete_incident", "{\"recordId\":1}")]
    [InlineData("read_incident", "{\"recordId\":2}")]
    [InlineData("list_categories", "{\"sql\":\"SELECT * FROM Officers\"}")]
    public async Task Unapproved_tool_calls_fail_closed(string tool, string args)
    {
        await using var f = new Fixture(); var provider = new Responses(Call(tool, args));
        var run = await f.Harness(provider).RunAsync(Summary(), 1, false, default);
        Assert.Equal("failed", run.Status); Assert.Null(run.Result); Assert.Single(provider.Requests);
    }
    [Fact] public async Task Endless_tool_loop_stops_after_four_model_turns()
    {
        await using var f = new Fixture(); var provider = new Responses(Call("list_categories", "{}"));
        var run = await f.Harness(provider).RunAsync(Summary(), 1, false, default);
        Assert.Equal("failed", run.Status); Assert.Equal(4, provider.Requests.Count); Assert.Contains("turn limit", run.Error);
    }
    [Fact] public async Task Invalid_provider_response_fails_without_falling_back_to_local()
    {
        await using var f = new Fixture(); var run = await f.Harness(new Responses("not-json")).RunAsync(Summary(), 1, false, default);
        Assert.Equal("failed", run.Status); Assert.Equal("openai", run.Mode); Assert.Null(run.Result);
    }
    [Fact] public async Task Forged_citations_are_not_saved_as_a_draft()
    {
        await using var f = new Fixture(); var run = await f.Harness(new Responses(Final(ValidResult() with { SourceRecordIds = [2] }))).RunAsync(Summary(), 1, false, default);
        Assert.Equal("failed", run.Status); Assert.Null(run.Result);
    }
    [Fact] public void Unknown_categories_and_invented_locations_are_rejected()
    {
        Assert.Throws<AgentFailure>(() => AgentHarness.ValidateResult(ValidResult() with { SuggestedCategory = "Unknown" }, 1, ["Lost and Found"], "Malanday hall"));
        Assert.Throws<AgentFailure>(() => AgentHarness.ValidateResult(ValidResult() with { Location = "Elsewhere" }, 1, ["Lost and Found"], "Malanday hall"));
    }
    [Fact] public async Task A_cancelled_run_remains_in_the_audit_store()
    {
        await using var f = new Fixture(); var provider = new CancellingResponse();
        var run = await f.Harness(provider).RunAsync(Summary(), 1, false, default);
        Assert.Equal("cancelled", run.Status); Assert.Equal("cancelled", (await f.Store.Runs.SingleAsync()).Status);
    }
    private sealed class CancellingResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => throw new OperationCanceledException();
    }
}
