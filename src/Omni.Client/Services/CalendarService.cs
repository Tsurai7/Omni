using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Models.Calendar;

namespace Omni.Client.Services;

public sealed class CalendarService
{
    private readonly HttpClient _http;
    private readonly IAuthService _authService;
    private readonly JsonSerializerOptions _jsonOptions;

    // Cached connection status (refreshed on each ConnectAsync / StatusAsync call)
    private bool _isConnected;
    private string? _connectedEmail;
    private DateTime? _lastSyncedAt;

    public bool IsConnected => _isConnected;
    public string? ConnectedEmail => _connectedEmail;
    public DateTime? LastSyncedAt => _lastSyncedAt;

    public event EventHandler? StatusChanged;

    public CalendarService(HttpClient http, IAuthService authService, JsonSerializerOptions jsonOptions)
    {
        _http = http;
        _authService = authService;
        _jsonOptions = jsonOptions;
    }

    // ── Auth ──────────────────────────────────────────────────────────────

    /// <summary>Returns the Google OAuth2 consent URL from the backend.</summary>
    public async Task<string?> GetAuthUrlAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenOrNullAsync(cancellationToken).ConfigureAwait(false);
        if (token == null) return null;

        var request = new HttpRequestMessage(HttpMethod.Get, "api/calendar/auth/google");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<CalendarAuthUrl>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            return result?.Url;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.GetAuthUrlAsync: {ex.Message}");
            return null;
        }
    }

    /// <summary>Sends the OAuth code to the backend to complete the connection.</summary>
    public async Task<bool> ConnectAsync(string code, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenOrNullAsync(cancellationToken).ConfigureAwait(false);
        if (token == null) return false;

        var body = JsonSerializer.Serialize(new { code }, _jsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, "api/calendar/auth/google/connect");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            _isConnected = true;
            await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.ConnectAsync: {ex.Message}");
            return false;
        }
    }

    /// <summary>Disconnects Google Calendar from the backend.</summary>
    public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenOrNullAsync(cancellationToken).ConfigureAwait(false);
        if (token == null) return false;

        var request = new HttpRequestMessage(HttpMethod.Delete, "api/calendar/auth/google");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _isConnected = false;
            _connectedEmail = null;
            _lastSyncedAt = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.DisconnectAsync: {ex.Message}");
            return false;
        }
    }

    /// <summary>Refreshes the connection status from the backend.</summary>
    public async Task<CalendarStatus?> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenOrNullAsync(cancellationToken).ConfigureAwait(false);
        if (token == null) return null;

        var request = new HttpRequestMessage(HttpMethod.Get, "api/calendar/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var status = await response.Content.ReadFromJsonAsync<CalendarStatus>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            if (status != null)
            {
                _isConnected = status.Connected;
                _connectedEmail = status.Email;
                if (status.LastSyncedAt != null && DateTime.TryParse(status.LastSyncedAt, out var lastSync))
                    _lastSyncedAt = lastSync;
            }
            return status;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.RefreshStatusAsync: {ex.Message}");
            return null;
        }
    }

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>Fetches unified calendar events for a date range.</summary>
    public async Task<List<CalendarEvent>> GetEventsAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenOrNullAsync(cancellationToken).ConfigureAwait(false);
        if (token == null) return new List<CalendarEvent>();

        var startStr = start.ToUniversalTime().ToString("O");
        var endStr   = end.ToUniversalTime().ToString("O");
        var request  = new HttpRequestMessage(HttpMethod.Get,
            $"api/calendar/events?start={Uri.EscapeDataString(startStr)}&end={Uri.EscapeDataString(endStr)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new List<CalendarEvent>();
            var body = await response.Content.ReadFromJsonAsync<CalendarEventsResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            return body?.Events.Select(e => e.ToCalendarEvent()).ToList() ?? new List<CalendarEvent>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.GetEventsAsync: {ex.Message}");
            return new List<CalendarEvent>();
        }
    }

    /// <summary>Triggers a manual sync on the backend.</summary>
    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenOrNullAsync(cancellationToken).ConfigureAwait(false);
        if (token == null) return false;

        var request = new HttpRequestMessage(HttpMethod.Post, "api/calendar/sync");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _lastSyncedAt = DateTime.Now;
                StatusChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.SyncAsync: {ex.Message}");
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<string?> GetTokenOrNullAsync(CancellationToken cancellationToken)
    {
        var token = await _authService.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
