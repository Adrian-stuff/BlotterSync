using System.ComponentModel.DataAnnotations;

namespace BlotterSync.Agent;

public sealed class ChatThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int OfficerId { get; set; }
    public int? RecordId { get; set; }
    public string Title { get; set; } = "";
    public string TurnsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; }
}

public sealed class ChatRequest
{
    [Required, StringLength(4000)]
    public string Message { get; set; } = "";
    public Guid? ThreadId { get; set; }
    [Range(1, int.MaxValue)] public int? RecordId { get; set; }
    [Range(1, ChatHarness.MaxTurns)] public int? Version { get; set; }
}

public sealed record ChatAnswer(string Reply, int[] SourceRecordIds);
public sealed record ChatTurn(string Question, string Reply, string Mode,
    int[] SourceRecordIds, IReadOnlyList<AgentStep> Steps, DateTime At);
public sealed record ChatThreadSummary(Guid Id, int? RecordId, string Title, DateTime UpdatedAt);
public sealed record ChatView(Guid Id, int? RecordId, string Title, int Version, IReadOnlyList<ChatTurn> Turns);
public sealed class ChatConflict : Exception;
