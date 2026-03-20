namespace Omni.Client.Models.Task;

/// <summary>Single task as returned by GET /tasks or POST /tasks.</summary>
public record TaskListItem(
    string Id = "",
    string UserId = "",
    string Title = "",
    string Status = "pending",
    string CreatedAt = "",
    string UpdatedAt = "");

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
    string CreatedAt = "",
    string UpdatedAt = "");

/// <summary>Task item for display in the UI (adds IsPending, StatusColor).</summary>
public record TaskDisplayItem(string Id, string UserId, string Title, string Status, string CreatedAt, string UpdatedAt)
{
    public bool IsPending => string.Equals(Status, "pending", StringComparison.OrdinalIgnoreCase);

    public Microsoft.Maui.Graphics.Color StatusColor => Status?.ToLowerInvariant() switch
    {
        "done"       => Microsoft.Maui.Graphics.Color.FromArgb("#4ECCA3"),
        "in_progress" => Microsoft.Maui.Graphics.Color.FromArgb("#F5A623"),
        _            => Microsoft.Maui.Graphics.Color.FromArgb("#66667A"),
    };

    public static TaskDisplayItem FromListItem(TaskListItem item) =>
        new(item.Id, item.UserId, item.Title, item.Status, item.CreatedAt, item.UpdatedAt);

    public static TaskDisplayItem FromLocalTask(LocalTask task) =>
        new(task.Id, "", task.Title, task.Status, task.CreatedAt.ToString("O"), task.UpdatedAt.ToString("O"));
}
