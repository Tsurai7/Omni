using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Models.Task;

namespace Omni.Client.Services;

public sealed class TaskService : ITaskService
{
    private readonly HttpClient _http;
    private readonly IAuthService _authService;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly LocalDatabaseService _localDb;

    public TaskService(HttpClient http, IAuthService authService, JsonSerializerOptions jsonOptions, LocalDatabaseService localDb)
    {
        _http = http;
        _authService = authService;
        _jsonOptions = jsonOptions;
        _localDb = localDb;
    }

    public async Task<IReadOnlyList<TaskListItem>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
            return Array.Empty<TaskListItem>();

        var request = new HttpRequestMessage(HttpMethod.Get, "api/tasks");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Array.Empty<TaskListItem>();
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Debug.WriteLine("TaskService.GetTasksAsync: 401 Unauthorized, clearing token.");
                _authService.Logout();
                return Array.Empty<TaskListItem>();
            }
            if (!response.IsSuccessStatusCode)
                return Array.Empty<TaskListItem>();

            try
            {
                var body = await response.Content.ReadFromJsonAsync<TaskListResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<TaskListItem>?)body?.Tasks ?? Array.Empty<TaskListItem>();
            }
            catch (JsonException)
            {
                return Array.Empty<TaskListItem>();
            }
        }
    }

    public async Task<TaskCreateResult?> CreateTaskAsync(string title, string priority = "medium", DateTime? dueDate = null, CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        title = (title ?? "").Trim();
        if (string.IsNullOrEmpty(title))
            return null;

        var dueDateStr = dueDate?.ToUniversalTime().ToString("O");

        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var body = JsonSerializer.Serialize(
                    new { title, status = "pending", priority, due_date = dueDateStr },
                    _jsonOptions);
                var request = new HttpRequestMessage(HttpMethod.Post, "api/tasks");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _authService.Logout();
                }
                else if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var created = await response.Content.ReadFromJsonAsync<TaskCreateResult>(_jsonOptions, cancellationToken).ConfigureAwait(false);
                        if (created != null && !string.IsNullOrEmpty(created.Id))
                            return created;
                    }
                    catch (JsonException) { }
                }
            }
            catch (HttpRequestException)
            {
                // Backend unreachable; fall through to save locally
            }
        }

        // Offline or failure: save locally
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

        var token = await _authService.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var body = JsonSerializer.Serialize(new { status }, _jsonOptions);
                var request = new HttpRequestMessage(HttpMethod.Patch, $"api/tasks/{taskId}/status");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    _authService.Logout();
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch (HttpRequestException)
            {
                // Backend unreachable; fall through to update local
            }
        }

        await _localDb.UpdateTaskStatusAsync(taskId, taskId, status, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> UpdateTaskAsync(string taskId, string title, string priority, DateTime? dueDate = default, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(title))
            return false;

        var token = await _authService.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var dueDateStr = dueDate.HasValue ? dueDate.Value.ToUniversalTime().ToString("O") : null;
                var body = JsonSerializer.Serialize(new { title, priority, due_date = dueDateStr }, _jsonOptions);
                var request = new HttpRequestMessage(HttpMethod.Put, $"api/tasks/{taskId}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    _authService.Logout();
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch (HttpRequestException)
            {
                // Backend unreachable; fall through to update local
            }
        }

        await _localDb.UpdateTaskAsync(taskId, title, priority, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId))
            return false;

        var token = await _authService.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, $"api/tasks/{taskId}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    _authService.Logout();
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    return true;
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await _localDb.DeleteTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                await _localDb.DeleteTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
        else
        {
            await _localDb.DeleteTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        return false;
    }
}
