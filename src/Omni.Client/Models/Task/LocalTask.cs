using SQLite;

namespace Omni.Client.Models.Task;

/// <summary>Local task entity for offline cache and sync.</summary>
[Table("tasks")]
public sealed class LocalTask
{
    [PrimaryKey]
    public string Id { get; set; } = "";

    /// <summary>Set after first successful sync to server.</summary>
    public string? ServerId { get; set; }

    public string Title { get; set; } = "";

    /// <summary>pending | done | cancelled</summary>
    public string Status { get; set; } = "pending";

    public bool IsSynced { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
