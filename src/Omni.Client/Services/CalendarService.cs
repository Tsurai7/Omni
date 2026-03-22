using System.Diagnostics;
using Omni.Client.Core.Abstractions.Api;
using Omni.Client.Models.Calendar;
using Refit;

namespace Omni.Client.Services;

public sealed class CalendarService
{
    private readonly ICalendarApi _api;

    private bool _isConnected;
    private string? _connectedEmail;
    private DateTime? _lastSyncedAt;

    public bool IsConnected => _isConnected;
    public string? ConnectedEmail => _connectedEmail;
    public DateTime? LastSyncedAt => _lastSyncedAt;

    public event EventHandler? StatusChanged;

    public CalendarService(ICalendarApi api)
    {
        _api = api;
    }

    public async Task<string?> GetAuthUrlAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _api.GetAuthUrlAsync(cancellationToken).ConfigureAwait(false);
            Debug.WriteLine($"CalendarService.GetAuthUrlAsync: URL={result?.Url}");
            return result?.Url;
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"CalendarService.GetAuthUrlAsync: HTTP {(int)ex.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.GetAuthUrlAsync: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ConnectAsync(string code, CancellationToken cancellationToken = default)
    {
        try
        {
            await _api.ConnectAsync(new { code }, cancellationToken).ConfigureAwait(false);
            _isConnected = true;
            await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"CalendarService.ConnectAsync: {ex.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.ConnectAsync: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _api.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            _isConnected = false;
            _connectedEmail = null;
            _lastSyncedAt = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"CalendarService.DisconnectAsync: {ex.StatusCode}");
            _isConnected = false;
            _connectedEmail = null;
            _lastSyncedAt = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.DisconnectAsync: {ex.Message}");
            return false;
        }
    }

    public async Task<CalendarStatus?> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _api.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status != null)
            {
                _isConnected = status.Connected;
                _connectedEmail = status.Email;
                if (status.LastSyncedAt != null && DateTime.TryParse(status.LastSyncedAt, out var lastSync))
                    _lastSyncedAt = lastSync;
            }
            return status;
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"CalendarService.RefreshStatusAsync: {ex.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.RefreshStatusAsync: {ex.Message}");
            return null;
        }
    }

    public async Task<List<CalendarEvent>> GetEventsAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        var startStr = start.ToUniversalTime().ToString("O");
        var endStr   = end.ToUniversalTime().ToString("O");

        try
        {
            var body = await _api.GetEventsAsync(startStr, endStr, cancellationToken).ConfigureAwait(false);
            return body?.Events.Select(e => e.ToCalendarEvent()).ToList() ?? new List<CalendarEvent>();
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"CalendarService.GetEventsAsync: {ex.StatusCode}");
            return new List<CalendarEvent>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.GetEventsAsync: {ex.Message}");
            return new List<CalendarEvent>();
        }
    }

    public async Task<bool> CreateGoogleEventAsync(
        string title, DateTime start, DateTime? end, bool isAllDay,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            title,
            description,
            start_at  = start.ToUniversalTime().ToString("O"),
            end_at    = end?.ToUniversalTime().ToString("O"),
            is_all_day = isAllDay,
        };

        try
        {
            await _api.CreateGoogleEventAsync(payload, cancellationToken).ConfigureAwait(false);
            await SyncAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"CalendarService.CreateGoogleEventAsync: HTTP {(int)ex.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.CreateGoogleEventAsync: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _api.SyncAsync(cancellationToken).ConfigureAwait(false);
            _lastSyncedAt = DateTime.Now;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"CalendarService.SyncAsync: {ex.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarService.SyncAsync: {ex.Message}");
            return false;
        }
    }
}
