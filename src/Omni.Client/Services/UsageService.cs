using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using Omni.Client.Abstractions;
using Omni.Client.Models.Usage;

namespace Omni.Client.Services;

public sealed class UsageService : IUsageService
{
    private const int SyncIntervalSeconds = 45;
    private const int FirstSyncDelaySeconds = 15;
    private readonly HttpClient _http;
    private readonly IAuthService _authService;
    private readonly IActiveWindowTracker _tracker;
    private readonly JsonSerializerOptions _jsonOptions;
    private Dictionary<string, long> _lastSyncedSeconds = new();
    private CancellationTokenSource? _syncCts;
    private readonly object _lock = new();

    public UsageService(HttpClient http, IAuthService authService, IActiveWindowTracker tracker, JsonSerializerOptions jsonOptions)
    {
        _http = http;
        _authService = authService;
        _tracker = tracker;
        _jsonOptions = jsonOptions;
    }

    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            Debug.WriteLine("UsageService.SyncAsync: no token, skip sync.");
            return false;
        }

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

        var request = new HttpRequestMessage(HttpMethod.Post, "api/usage/sync");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new UsageSyncRequest { Entries = entries }, options: _jsonOptions);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"UsageService.SyncAsync: sync failed {response.StatusCode}");
            lock (_lock)
            {
                foreach (var e in entries)
                    _lastSyncedSeconds[e.AppName] = _lastSyncedSeconds.GetValueOrDefault(e.AppName, 0) - e.DurationSeconds;
            }
            return false;
        }
        return true;
    }

    public async Task<UsageListResponse?> GetUsageAsync(string? from = null, string? to = null, string? groupBy = null, string? category = null, string? appName = null, CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
            return null;

        var url = "api/usage";
        var q = new List<string>();
        if (!string.IsNullOrEmpty(from)) q.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrEmpty(to)) q.Add($"to={Uri.EscapeDataString(to)}");
        if (!string.IsNullOrEmpty(groupBy)) q.Add($"group_by={Uri.EscapeDataString(groupBy)}");
        if (!string.IsNullOrEmpty(category)) q.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrEmpty(appName)) q.Add($"app_name={Uri.EscapeDataString(appName)}");
        if (q.Count > 0) url += "?" + string.Join("&", q);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        try
        {
            var body = await response.Content.ReadFromJsonAsync<UsageListResponse>(_jsonOptions, cancellationToken);
            return body;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void StartPeriodicSync()
    {
        _tracker.StartTracking(); // ensure tracking runs even if user never opened Home
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
        // First sync after a short delay so the tracker has time to accumulate usage
        await Task.Delay(TimeSpan.FromSeconds(FirstSyncDelaySeconds), cancellationToken);
        if (cancellationToken.IsCancellationRequested) return;
        try { await SyncAsync(cancellationToken); } catch (OperationCanceledException) { return; } catch { /* ignore */ }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(SyncIntervalSeconds), cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            try { await SyncAsync(cancellationToken); }
            catch (OperationCanceledException) { break; }
            catch { /* ignore */ }
        }
    }
}
