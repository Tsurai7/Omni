using System.Diagnostics;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Core.Abstractions.Api;
using Omni.Client.Models.Task;
using Omni.Client.Models.Usage;
using Omni.Client.Models.Session;
using Refit;

namespace Omni.Client.Services;

public sealed class SyncService : ISyncService
{
    private const int SyncIntervalSeconds = 45;
    private readonly ITaskApi _taskApi;
    private readonly IUsageApi _usageApi;
    private readonly ISessionApi _sessionApi;
    private readonly LocalDatabaseService _localDb;
    private readonly JsonSerializerOptions _jsonOptions;
    private CancellationTokenSource? _syncCts;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public SyncService(
        ITaskApi taskApi,
        IUsageApi usageApi,
        ISessionApi sessionApi,
        LocalDatabaseService localDb,
        JsonSerializerOptions jsonOptions)
    {
        _taskApi = taskApi;
        _usageApi = usageApi;
        _sessionApi = sessionApi;
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

    public async Task RunSyncOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            var pending = await _localDb.GetUnsyncedPendingAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in pending)
            {
                if (cancellationToken.IsCancellationRequested) return;
                var success = await SendPendingRowAsync(row, cancellationToken).ConfigureAwait(false);
                if (success)
                    await _localDb.MarkPendingSyncedAsync(row.Id, cancellationToken).ConfigureAwait(false);
            }

            var tasks = await _localDb.GetUnsyncedTasksAsync(cancellationToken).ConfigureAwait(false);
            foreach (var task in tasks)
            {
                if (cancellationToken.IsCancellationRequested) return;
                var success = await SendTaskAsync(task, cancellationToken).ConfigureAwait(false);
                if (success)
                    await _localDb.MarkTaskSyncedAsync(task.Id, task.ServerId, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<bool> SendPendingRowAsync(PendingSyncRow row, CancellationToken cancellationToken)
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

            try
            {
                await _usageApi.SyncAsync(req, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (ApiException)
            {
                return false;
            }
            catch (HttpRequestException)
            {
                return false;
            }
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

            try
            {
                await _sessionApi.SyncSessionsAsync(req, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (ApiException)
            {
                return false;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        return false;
    }

    private async Task<bool> SendTaskAsync(LocalTask task, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(task.ServerId))
        {
            try
            {
                var created = await _taskApi.CreateTaskAsync(
                    new { title = task.Title, status = task.Status },
                    cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(created?.Id))
                {
                    task.ServerId = created.Id;
                    await _localDb.MarkTaskSyncedAsync(task.Id, created.Id, cancellationToken).ConfigureAwait(false);
                }
                return true;
            }
            catch (ApiException)
            {
                return false;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        try
        {
            await _taskApi.UpdateStatusAsync(task.ServerId, new { status = task.Status }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
