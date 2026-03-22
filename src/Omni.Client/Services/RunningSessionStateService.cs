using Microsoft.Maui.Dispatching;
using Omni.Client.Abstractions;

namespace Omni.Client.Services;

public sealed class RunningSessionStateService : IRunningSessionState
{
    private readonly object _lock = new();
    private readonly ISessionDistractionService _distraction;
    private readonly INotificationManager _notificationManager;
    private bool _isRunning;
    private DateTime _startTimeUtc;
    private DateTime? _endTimeUtc;
    private string _activityName = "";
    private string _activityType = "work";
    private IDispatcherTimer? _timer;

    public bool IsRunning { get { lock (_lock) return _isRunning; } }
    public DateTime StartTimeUtc { get { lock (_lock) return _startTimeUtc; } }
    public DateTime? EndTimeUtc { get { lock (_lock) return _endTimeUtc; } }
    public string ActivityName { get { lock (_lock) return _activityName; } }
    public string ActivityType { get { lock (_lock) return _activityType; } }

    public RunningSessionStateService(
        ISessionDistractionService distraction,
        INotificationManager notificationManager)
    {
        _distraction = distraction ?? throw new ArgumentNullException(nameof(distraction));
        _notificationManager = notificationManager ?? throw new ArgumentNullException(nameof(notificationManager));
    }

    public event Action<int?>? Tick;
    public event Action? CountdownReachedZero;

    public void Start(string activityName, string activityType, int? durationSeconds)
    {
        DateTime startTimeUtc;
        lock (_lock)
        {
            if (_isRunning) return;
            _activityName = activityName;
            _activityType = activityType;
            _startTimeUtc = DateTime.UtcNow;
            startTimeUtc = _startTimeUtc;
            _endTimeUtc = durationSeconds.HasValue && durationSeconds.Value > 0
                ? _startTimeUtc.AddSeconds(durationSeconds.Value)
                : null;
            _isRunning = true;
        }
        _distraction.Start(startTimeUtc, activityName);
        _ = _notificationManager.RequestPermissionAsync();
        StartTimer();
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isRunning = false;
        }
        _distraction.Stop();
        StopTimer();
    }

    public int? GetRemainingSeconds()
    {
        lock (_lock)
        {
            if (!_isRunning || !_endTimeUtc.HasValue) return null;
            var remaining = (_endTimeUtc.Value - DateTime.UtcNow).TotalSeconds;
            return remaining <= 0 ? 0 : (int)remaining;
        }
    }

    public int GetElapsedSeconds()
    {
        lock (_lock)
        {
            if (!_isRunning) return 0;
            return (int)(DateTime.UtcNow - _startTimeUtc).TotalSeconds;
        }
    }

    private void StartTimer()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void OnTick()
    {
        if (!IsRunning)
        {
            StopTimer();
            return;
        }
        int? remaining = GetRemainingSeconds();
        if (remaining.HasValue && remaining.Value <= 0)
        {
            MainThread.BeginInvokeOnMainThread(() => CountdownReachedZero?.Invoke());
            return;
        }
        Tick?.Invoke(remaining);
    }
}
