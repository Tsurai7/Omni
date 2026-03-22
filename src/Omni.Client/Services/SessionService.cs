using System.Diagnostics;
using Omni.Client.Abstractions;
using Omni.Client.Core.Abstractions.Api;
using Omni.Client.Models.Session;
using Refit;

namespace Omni.Client.Services;

public sealed class SessionService : ISessionService
{
    private readonly ISessionApi _api;
    private readonly LocalDatabaseService _localDb;

    public SessionService(ISessionApi api, LocalDatabaseService localDb)
    {
        _api = api;
        _localDb = localDb;
    }

    public async Task<bool> SyncSessionsAsync(IReadOnlyList<SessionSyncEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries == null || entries.Count == 0)
            return true;

        var syncRequest = new SessionSyncRequest { Entries = entries.ToList() };

        try
        {
            await _api.SyncSessionsAsync(syncRequest, cancellationToken);
            return true;
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"SessionService.SyncSessionsAsync: {ex.StatusCode}");
            await SavePendingAsync(syncRequest, cancellationToken);
            return false;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"SessionService.SyncSessionsAsync: network error: {ex.Message}");
            await SavePendingAsync(syncRequest, cancellationToken);
            return false;
        }
    }

    private async Task SavePendingAsync(SessionSyncRequest syncRequest, CancellationToken cancellationToken)
    {
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(syncRequest);
            await _localDb.SavePendingSyncAsync("session", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SessionService.SavePendingAsync: {ex.Message}");
        }
    }

    public async Task<SessionListResponse?> GetSessionsAsync(string? from = null, string? to = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _api.GetSessionsAsync(from, to, cancellationToken);
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"SessionService.GetSessionsAsync: {ex.StatusCode}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"SessionService.GetSessionsAsync: network error: {ex.Message}");
            return null;
        }
    }
}
