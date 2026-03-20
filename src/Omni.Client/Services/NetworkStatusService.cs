using System.Diagnostics;
using System.Net.Http;

namespace Omni.Client.Services;

/// <summary>
/// Lightweight connectivity tracker. Pages observe IsOnline to show/hide error banners.
/// </summary>
public sealed class NetworkStatusService
{
    private static readonly Lazy<NetworkStatusService> _instance =
        new(() => new NetworkStatusService());
    public static NetworkStatusService Instance => _instance.Value;

    private bool _isOnline = true;

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (_isOnline == value) return;
            _isOnline = value;
            OnlineStatusChanged?.Invoke(this, value);
        }
    }

    /// <summary>Fires on the UI thread whenever online state flips.</summary>
    public event EventHandler<bool>? OnlineStatusChanged;

    private NetworkStatusService() { }

    /// <summary>
    /// Call after each HTTP call. Reports whether the call succeeded and updates IsOnline.
    /// Returns the same exception for caller convenience.
    /// </summary>
    public void ReportSuccess() => IsOnline = true;

    public void ReportFailure(Exception ex)
    {
        if (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
        {
            Debug.WriteLine($"[NetworkStatus] Connectivity failure: {ex.Message}");
            IsOnline = false;
        }
        // Non-network failures (JSON parse, auth) don't flip offline
    }

    /// <summary>
    /// Wraps an async data-load with success/failure tracking.
    /// Returns the result or null on failure.
    /// </summary>
    public static async Task<T?> WrapAsync<T>(Func<Task<T?>> fn) where T : class
    {
        try
        {
            var result = await fn();
            Instance.ReportSuccess();
            return result;
        }
        catch (Exception ex)
        {
            Instance.ReportFailure(ex);
            return null;
        }
    }
}
