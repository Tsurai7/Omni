namespace Omni.Client.Models.Task;

/// <summary>Single task as returned by GET /tasks or POST /tasks.</summary>
public record TaskListItem(
    string Id = "",
    string UserId = "",
    string Title = "",
    string Status = "pending",
    string Priority = "medium",
    string CreatedAt = "",
    string UpdatedAt = "",
    string? DueDate = null,
    string? ScheduledFor = null,
    string? GoogleEventId = null);

/// <summary>Response for GET /tasks.</summary>
public record TaskListResponse
{
    public List<TaskListItem> Tasks { get; init; } = new();
}

/// <summary>Result of creating a task (same shape as one task).</summary>
public record TaskCreateResult(
    string Id = "",
    string UserId = "",
    string Title = "",
    string Status = "pending",
    string Priority = "medium",
    string CreatedAt = "",
    string UpdatedAt = "",
    string? DueDate = null,
    string? ScheduledFor = null,
    string? GoogleEventId = null);

/// <summary>Task item for display in the UI.</summary>
public record TaskDisplayItem(string Id, string UserId, string Title, string Status, string Priority,
    string CreatedAt, string UpdatedAt,
    string? DueDate = null, string? ScheduledFor = null, string? GoogleEventId = null)
{
    public bool IsPending     => string.Equals(Status, "pending",     StringComparison.OrdinalIgnoreCase);
    public bool IsInProgress  => string.Equals(Status, "in_progress", StringComparison.OrdinalIgnoreCase);
    public bool IsDone        => string.Equals(Status, "done",        StringComparison.OrdinalIgnoreCase);

    public DateTime? DueDateParsed => DueDate != null && DateTime.TryParse(DueDate, out var dt) ? dt : null;

    public bool HasDueDate => DueDateParsed.HasValue;

    public bool IsOverdue => DueDateParsed.HasValue && DueDateParsed.Value.Date < DateTime.Today && !IsDone;

    public string DueDateLabel
    {
        get
        {
            if (!DueDateParsed.HasValue) return "";
            var d = DueDateParsed.Value.Date;
            if (d == DateTime.Today) return "Today";
            if (d == DateTime.Today.AddDays(1)) return "Tomorrow";
            if (d == DateTime.Today.AddDays(-1)) return "Yesterday";
            return d.ToString("MMM d");
        }
    }

    public Microsoft.Maui.Graphics.Color DueDateColor =>
        IsOverdue
            ? Microsoft.Maui.Graphics.Color.FromArgb("#FF5C5C")
            : Microsoft.Maui.Graphics.Color.FromArgb("#66667A");

    public Microsoft.Maui.Graphics.Color StatusColor => Status?.ToLowerInvariant() switch
    {
        "done"        => Microsoft.Maui.Graphics.Color.FromArgb("#4ECCA3"),
        "in_progress" => Microsoft.Maui.Graphics.Color.FromArgb("#F5A623"),
        _             => Microsoft.Maui.Graphics.Color.FromArgb("#66667A"),
    };

    public Microsoft.Maui.Graphics.Color PriorityColor => Priority?.ToLowerInvariant() switch
    {
        "high"   => Microsoft.Maui.Graphics.Color.FromArgb("#FF5C5C"),
        "low"    => Microsoft.Maui.Graphics.Color.FromArgb("#4ECCA3"),
        _        => Microsoft.Maui.Graphics.Color.FromArgb("#F5A623"), // medium
    };

    public string PriorityLabel => Priority?.ToLowerInvariant() switch
    {
        "high" => "HIGH",
        "low"  => "LOW",
        _      => "MED",
    };

    public static TaskDisplayItem FromListItem(TaskListItem item) =>
        new(item.Id, item.UserId, item.Title, item.Status, item.Priority, item.CreatedAt, item.UpdatedAt,
            item.DueDate, item.ScheduledFor, item.GoogleEventId);

    public static TaskDisplayItem FromLocalTask(LocalTask task) =>
        new(task.Id, "", task.Title, task.Status, task.Priority,
            task.CreatedAt.ToString("O"), task.UpdatedAt.ToString("O"),
            task.DueDate?.ToString("O"), task.ScheduledFor?.ToString("O"), task.GoogleEventId);
}
