using Microsoft.EntityFrameworkCore;

namespace BlotterSync.Agent;

// Kept separate from cloud-synced incident entities. A review does not mutate a blotter.
public sealed class AgentRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int OfficerId { get; set; }
    public string Workflow { get; set; } = "";
    public int? RecordId { get; set; }
    public string Status { get; set; } = "running";
    public string Mode { get; set; } = "local";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? Error { get; set; }
    public string? SourceNotes { get; set; }
    public string? ResultJson { get; set; }
    public string StepsJson { get; set; } = "[]";
}
public sealed class AgentStore(DbContextOptions<AgentStore> options) : DbContext(options)
{
    public DbSet<AgentRun> Runs => Set<AgentRun>();
    public DbSet<ChatThread> ChatThreads => Set<ChatThread>();
    // Additive upgrade for audit stores created before intake source snapshots.
    // Existing runs are retained; older intakes correctly have no source snapshot.
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await Database.EnsureCreatedAsync(ct);
        var connection = Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info('Runs');";
            bool hasSourceNotes = false;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    hasSourceNotes |= reader.GetString(1) == nameof(AgentRun.SourceNotes);
            }
            if (!hasSourceNotes)
                await Database.ExecuteSqlRawAsync("ALTER TABLE \"Runs\" ADD COLUMN \"SourceNotes\" TEXT NULL;", ct);
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "ChatThreads" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "OfficerId" INTEGER NOT NULL,
                    "RecordId" INTEGER NULL,
                    "Title" TEXT NOT NULL,
                    "TurnsJson" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    "Version" INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_ChatThreads_OfficerId_UpdatedAt"
                    ON "ChatThreads" ("OfficerId", "UpdatedAt");
                """, ct);
        }
        finally { if (openedHere) await Database.CloseConnectionAsync(); }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AgentRun>().HasIndex(r => new { r.OfficerId, r.CreatedAt });
        var chat = builder.Entity<ChatThread>();
        chat.HasIndex(r => new { r.OfficerId, r.UpdatedAt });
        chat.Property(r => r.Version).IsConcurrencyToken();
    }
}
