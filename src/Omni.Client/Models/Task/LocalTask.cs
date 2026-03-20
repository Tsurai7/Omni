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

    /// <summary>pending | in_progress | done | cancelled</summary>
    public string Status { get; set; } = "pending";

    /// <summary>low | medium | high</summary>
    public string Priority { get; set; } = "medium";

    public bool IsSynced { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Optional due date for calendar display.</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Optional scheduled time (more precise than due date).</summary>
    public DateTime? ScheduledFor { get; set; }

    /// <summary>Google Calendar event ID if synced.</summary>
    public string? GoogleEventId { get; set; }
}
