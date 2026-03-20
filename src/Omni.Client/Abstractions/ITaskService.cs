using Omni.Client.Models.Task;

namespace Omni.Client.Abstractions;

public interface ITaskService
{
    Task<IReadOnlyList<TaskListItem>> GetTasksAsync(CancellationToken cancellationToken = default);
    Task<TaskCreateResult?> CreateTaskAsync(string title, string priority = "medium", CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(string taskId, string status, CancellationToken cancellationToken = default);
    Task<bool> UpdateTaskAsync(string taskId, string title, string priority, CancellationToken cancellationToken = default);
    Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default);
}
