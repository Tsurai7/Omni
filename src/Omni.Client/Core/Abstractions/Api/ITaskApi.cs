using Omni.Client.Models.Task;
using Refit;

namespace Omni.Client.Core.Abstractions.Api;

public interface ITaskApi
{
    [Get("/api/tasks")]
    Task<TaskListResponse> GetTasksAsync(CancellationToken ct = default);

    [Post("/api/tasks")]
    Task<TaskCreateResult> CreateTaskAsync([Body] object request, CancellationToken ct = default);

    [Put("/api/tasks/{id}")]
    Task<TaskListItem> UpdateTaskAsync(string id, [Body] object request, CancellationToken ct = default);

    [Patch("/api/tasks/{id}/status")]
    Task<TaskListItem> UpdateStatusAsync(string id, [Body] object request, CancellationToken ct = default);

    [Delete("/api/tasks/{id}")]
    Task DeleteTaskAsync(string id, CancellationToken ct = default);
}
