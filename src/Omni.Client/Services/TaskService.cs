using Omni.Client.Abstractions;
using Omni.Client.Core.Abstractions.Api;
using Omni.Client.Models.Task;
using Refit;

namespace Omni.Client.Services;

public sealed class TaskService : ITaskService
{
    private readonly ITaskApi _api;
    private readonly LocalDatabaseService _localDb;

    public TaskService(
        ITaskApi api,
        LocalDatabaseService localDb)
    {
        _api = api;
        _localDb = localDb;
    }

    public async Task<IReadOnlyList<TaskListItem>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var body = await _api.GetTasksAsync(cancellationToken);
            return (IReadOnlyList<TaskListItem>?)body?.Tasks ?? Array.Empty<TaskListItem>();
        }
        catch (ApiException)
        {
            return Array.Empty<TaskListItem>();
        }
        catch (HttpRequestException)
        {
            return Array.Empty<TaskListItem>();
        }
    }

    public async Task<TaskCreateResult?> CreateTaskAsync(string title, string priority = "medium", DateTime? dueDate = null, CancellationToken cancellationToken = default)
    {
        title = (title ?? "").Trim();
        if (string.IsNullOrEmpty(title))
            return null;

        var dueDateStr = dueDate?.ToUniversalTime().ToString("O");

        try
        {
            var created = await _api.CreateTaskAsync(
                new { title, status = "pending", priority, due_date = dueDateStr },
                cancellationToken);
            if (created != null && !string.IsNullOrEmpty(created.Id))
                return created;
        }
        catch (ApiException)
        {
        }
        catch (HttpRequestException)
        {
        }

        var localId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var localTask = new LocalTask
        {
            Id = localId,
            Title = title,
            Status = "pending",
            Priority = priority,
            DueDate = dueDate,
            IsSynced = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _localDb.InsertTaskAsync(localTask, cancellationToken).ConfigureAwait(false);
        return new TaskCreateResult(
            Id: localId,
            UserId: "",
            Title: title,
            Status: "pending",
            Priority: priority,
            CreatedAt: now.ToString("O"),
            UpdatedAt: now.ToString("O"),
            DueDate: dueDateStr);
    }

    public async Task<bool> UpdateStatusAsync(string taskId, string status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(status))
            return false;

        try
        {
            await _api.UpdateStatusAsync(taskId, new { status }, cancellationToken);
            return true;
        }
        catch (ApiException)
        {
        }
        catch (HttpRequestException)
        {
        }

        await _localDb.UpdateTaskStatusAsync(taskId, taskId, status, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> UpdateTaskAsync(string taskId, string title, string priority, DateTime? dueDate = default, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(title))
            return false;

        var dueDateStr = dueDate.HasValue ? dueDate.Value.ToUniversalTime().ToString("O") : null;

        try
        {
            await _api.UpdateTaskAsync(taskId, new { title, priority, due_date = dueDateStr }, cancellationToken);
            return true;
        }
        catch (ApiException)
        {
        }
        catch (HttpRequestException)
        {
        }

        await _localDb.UpdateTaskAsync(taskId, title, priority, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId))
            return false;

        try
        {
            await _api.DeleteTaskAsync(taskId, cancellationToken);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await _localDb.DeleteTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            await _localDb.DeleteTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
