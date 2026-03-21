using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using Omni.Client.Abstractions;
using Omni.Client.Models.Session;

namespace Omni.Client.Services;

public sealed class SessionService : ISessionService
{
    private readonly HttpClient _http;
    private readonly IAuthService _authService;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly LocalDatabaseService _localDb;

    public SessionService(HttpClient http, IAuthService authService, JsonSerializerOptions jsonOptions, LocalDatabaseService localDb)
    {
        _http = http;
        _authService = authService;
        _jsonOptions = jsonOptions;
        _localDb = localDb;
    }

    public async Task<bool> SyncSessionsAsync(IReadOnlyList<SessionSyncEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries == null || entries.Count == 0)
            return true;

        var token = await _authService.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            Debug.WriteLine("SessionService.SyncSessionsAsync: no token, skip sync.");
            return false;
        }

        var syncRequest = new SessionSyncRequest { Entries = entries.ToList() };
        var request = new HttpRequestMessage(HttpMethod.Post, "api/sessions/sync");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(syncRequest, options: _jsonOptions);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Debug.WriteLine("SessionService.SyncSessionsAsync: 401 Unauthorized, clearing token.");
                _authService.Logout();
            }
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"SessionService.SyncSessionsAsync: sync failed {response.StatusCode}");
                try
                {
                    var payload = JsonSerializer.Serialize(syncRequest, _jsonOptions);
                    await _localDb.SavePendingSyncAsync("session", payload, cancellationToken);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SessionService.SyncSessionsAsync: failed to save pending sync: {ex.Message}");
                }
                return false;
            }
            return true;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"SessionService.SyncSessionsAsync: network error: {ex.Message}");
            try
            {
                var payload = JsonSerializer.Serialize(syncRequest, _jsonOptions);
                await _localDb.SavePendingSyncAsync("session", payload, cancellationToken);
            }
            catch (Exception dbEx)
            {
                Debug.WriteLine($"SessionService.SyncSessionsAsync: failed to save pending sync: {dbEx.Message}");
            }
            return false;
        }
    }

    public async Task<SessionListResponse?> GetSessionsAsync(string? from = null, string? to = null, CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
            return null;

        var url = "api/sessions";
        var q = new List<string>();
        if (!string.IsNullOrEmpty(from)) q.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrEmpty(to)) q.Add($"to={Uri.EscapeDataString(to)}");
        if (q.Count > 0) url += "?" + string.Join("&", q);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Debug.WriteLine("SessionService.GetSessionsAsync: 401 Unauthorized, clearing token.");
                _authService.Logout();
            }
            if (!response.IsSuccessStatusCode)
                return null;
            try
            {
                var body = await response.Content.ReadFromJsonAsync<SessionListResponse>(_jsonOptions, cancellationToken);
                return body;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"SessionService.GetSessionsAsync: network error: {ex.Message}");
            return null;
        }
    }
}
