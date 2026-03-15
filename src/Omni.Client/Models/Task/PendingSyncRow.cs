using SQLite;

namespace Omni.Client.Models.Task;

/// <summary>Pending telemetry (usage or session) batch to sync to the API.</summary>
[Table("pending_sync")]
public sealed class PendingSyncRow
{
    [PrimaryKey]
    public string Id { get; set; } = "";

    /// <summary>usage | session</summary>
    public string Kind { get; set; } = "";

    public string Payload { get; set; } = "";

    public bool IsSynced { get; set; }

    public DateTime CreatedAt { get; set; }
}
