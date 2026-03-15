using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Models.Task;
using Omni.Client.Models.Usage;
using Omni.Client.Models.Session;

namespace Omni.Client.Services;

public sealed class SyncService : ISyncService
{
    private const int SyncIntervalSeconds = 45;
    private readonly HttpClient _http;
    private readonly IAuthService _authService;
    private readonly LocalDatabaseService _localDb;
    private readonly JsonSerializerOptions _jsonOptions;
    private CancellationTokenSource? _syncCts;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public SyncService(HttpClient http, IAuthService authService, LocalDatabaseService localDb, JsonSerializerOptions jsonOptions)
    {
        _http = http;
        _authService = authService;
        _localDb = localDb;
        _jsonOptions = jsonOptions;
    }

    public void StartPeriodicSync()
    {
        StopPeriodicSync();
        _syncCts = new CancellationTokenSource();
        var token = _syncCts.Token;
        _ = RunSyncLoopAsync(token);
    }

    public void StopPeriodicSync()
    {
        _syncCts?.Cancel();
        _syncCts = null;
    }

    private async Task RunSyncLoopAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) return;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSyncOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SyncService.RunSyncLoopAsync: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(SyncIntervalSeconds), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Single sync pass: drain pending_sync and unsynced tasks.</summary>
    public async Task RunSyncOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            var token = await _authService.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
                return;

            // Drain pending telemetry (usage / session)
            var pending = await _localDb.GetUnsyncedPendingAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in pending)
            {
                if (cancellationToken.IsCancellationRequested) return;
                var success = await SendPendingRowAsync(row, token, cancellationToken).ConfigureAwait(false);
                if (success)
                    await _localDb.MarkPendingSyncedAsync(row.Id, cancellationToken).ConfigureAwait(false);
            }

            // Drain unsynced tasks
            var tasks = await _localDb.GetUnsyncedTasksAsync(cancellationToken).ConfigureAwait(false);
            foreach (var task in tasks)
            {
                if (cancellationToken.IsCancellationRequested) return;
                var success = await SendTaskAsync(task, token, cancellationToken).ConfigureAwait(false);
                if (success)
                    await _localDb.MarkTaskSyncedAsync(task.Id, task.ServerId, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<bool> SendPendingRowAsync(PendingSyncRow row, string bearerToken, CancellationToken cancellationToken)
    {
        if (row.Kind == "usage")
        {
            UsageSyncRequest? req;
            try
            {
                req = JsonSerializer.Deserialize<UsageSyncRequest>(row.Payload, _jsonOptions);
            }
            catch (JsonException)
            {
                return false;
            }
            if (req?.Entries == null || req.Entries.Count == 0)
                return true;

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/usage/sync");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            request.Content = new StringContent(row.Payload, Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        if (row.Kind == "session")
        {
            SessionSyncRequest? req;
            try
            {
                req = JsonSerializer.Deserialize<SessionSyncRequest>(row.Payload, _jsonOptions);
            }
            catch (JsonException)
            {
                return false;
            }
            if (req?.Entries == null || req.Entries.Count == 0)
                return true;

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/sessions/sync");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            request.Content = new StringContent(row.Payload, Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        return false;
    }

    private async Task<bool> SendTaskAsync(LocalTask task, string bearerToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(task.ServerId))
        {
            // Create task
            var body = JsonSerializer.Serialize(new { title = task.Title, status = task.Status }, _jsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/tasks");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;
            try
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("id", out var idEl))
                {
                    var serverId = idEl.GetString();
                    if (!string.IsNullOrEmpty(serverId))
                    {
                        task.ServerId = serverId;
                        await _localDb.MarkTaskSyncedAsync(task.Id, serverId, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // still consider synced to avoid retry loop
            }
            return true;
        }

        // Update status
        var patchBody = JsonSerializer.Serialize(new { status = task.Status }, _jsonOptions);
        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"api/tasks/{task.ServerId}/status");
        patchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        patchRequest.Content = new StringContent(patchBody, Encoding.UTF8, "application/json");
        using var patchResponse = await _http.SendAsync(patchRequest, cancellationToken).ConfigureAwait(false);
        return patchResponse.IsSuccessStatusCode;
    }
}
