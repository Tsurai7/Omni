using Omni.Client.Models.Task;

namespace Omni.Client.Abstractions;

public interface ITaskService
{
    Task<IReadOnlyList<TaskListItem>> GetTasksAsync(CancellationToken cancellationToken = default);
    Task<TaskCreateResult?> CreateTaskAsync(string title, CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(string taskId, string status, CancellationToken cancellationToken = default);
    Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default);
}
