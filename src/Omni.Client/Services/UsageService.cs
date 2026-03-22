using System.Diagnostics;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Core.Abstractions.Api;
using Omni.Client.Models.Usage;
using Refit;

namespace Omni.Client.Services;

public sealed class UsageService : IUsageService
{
    private const int SyncIntervalSeconds = 45;
    private const int FirstSyncDelaySeconds = 15;
    private readonly IUsageApi _api;
    private readonly IActiveWindowTracker _tracker;
    private readonly LocalDatabaseService _localDb;
    private Dictionary<string, long> _lastSyncedSeconds = new();
    private CancellationTokenSource? _syncCts;
    private readonly object _lock = new();

    public UsageService(
        IUsageApi api,
        IActiveWindowTracker tracker,
        LocalDatabaseService localDb)
    {
        _api = api;
        _tracker = tracker;
        _localDb = localDb;
    }

    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        var usage = _tracker.GetAppUsage();
        var entries = new List<UsageSyncEntry>();
        lock (_lock)
        {
            foreach (var kv in usage)
            {
                var totalSec = (long)kv.Value.TotalSeconds;
                if (totalSec <= 0) continue;
                var last = _lastSyncedSeconds.GetValueOrDefault(kv.Key, 0L);
                var delta = totalSec - last;
                if (delta <= 0) continue;
                entries.Add(new UsageSyncEntry
                {
                    AppName = kv.Key,
                    Category = CategoryResolver.ResolveCategory(kv.Key),
                    DurationSeconds = delta
                });
                _lastSyncedSeconds[kv.Key] = totalSec;
            }
        }
        if (entries.Count == 0)
            return true;

        var syncRequest = new UsageSyncRequest(entries);
        try
        {
            await _api.SyncAsync(syncRequest, cancellationToken);
            return true;
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"UsageService.SyncAsync: {ex.StatusCode}");
            await SavePendingAsync(syncRequest, cancellationToken);
            return false;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"UsageService.SyncAsync: network error: {ex.Message}");
            await SavePendingAsync(syncRequest, cancellationToken);
            return false;
        }
    }

    private async Task SavePendingAsync(UsageSyncRequest syncRequest, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(syncRequest);
            await _localDb.SavePendingSyncAsync("usage", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UsageService.SavePendingAsync: {ex.Message}");
        }
    }

    public async Task<UsageListResponse?> GetUsageAsync(string? from = null, string? to = null, string? groupBy = null, string? category = null, string? appName = null, int utcOffsetMinutes = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _api.GetUsageAsync(utcOffsetMinutes, from, to, groupBy, category, appName, cancellationToken);
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"UsageService.GetUsageAsync: {ex.StatusCode}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"UsageService.GetUsageAsync: network error: {ex.Message}");
            return null;
        }
    }

    public void StartPeriodicSync()
    {
        _tracker.StartTracking();
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
        await Task.Delay(TimeSpan.FromSeconds(FirstSyncDelaySeconds), cancellationToken);
        if (cancellationToken.IsCancellationRequested) return;
        try { await SyncAsync(cancellationToken); } catch (OperationCanceledException) { return; } catch { }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(SyncIntervalSeconds), cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            try { await SyncAsync(cancellationToken); }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }
}
