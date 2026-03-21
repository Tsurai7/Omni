using System.Diagnostics;
using Omni.Client.Abstractions;

namespace Omni.Client.Services;

public sealed class SessionDistractionService : ISessionDistractionService
{
    private readonly IActiveWindowTracker _tracker;
    private readonly INotificationManager _notificationManager;
    private readonly DistractionConfig _config;
    private readonly object _lock = new();

    private DateTime _sessionStartTimeUtc;
    private string _activityName = "";
    private bool _isRunning;
    private IDispatcherTimer? _pollTimer;
    private string _lastApp = "";
    private string _lastCategory = "";
    private DateTime? _distractingSegmentStartUtc;
    private double _distractingSeconds;
    private int _distractionEventCount;
    private DateTime? _lastNotificationUtc;
    private readonly List<DateTime> _switchTimestamps = new();

    public event Action<DistractionEvent>? DistractionDetected;
    public event Action<SessionScoreResult>? SessionEndedWithScore;

    public bool IsRunning
    {
        get { lock (_lock) return _isRunning; }
    }

    public SessionDistractionService(
        IActiveWindowTracker tracker,
        INotificationManager notificationManager,
        DistractionConfig config)
    {
        _tracker = tracker;
        _notificationManager = notificationManager;
        _config = config;
    }

    public void Start(DateTime sessionStartTimeUtc, string activityName)
    {
        lock (_lock)
        {
            if (_isRunning) return;
            _sessionStartTimeUtc = sessionStartTimeUtc;
            _activityName = activityName ?? "";
            _isRunning = true;
            Debug.WriteLine($"[SessionDistraction] Started: activity=\"{_activityName}\", windowMinutes={_config.FrequentSwitchWindowMinutes}, threshold={_config.FrequentSwitchThreshold}, debounceMinutes={_config.NotificationDebounceMinutes}");
            _lastApp = "";
            _lastCategory = "";
            _distractingSegmentStartUtc = null;
            _distractingSeconds = 0;
            _distractionEventCount = 0;
            _lastNotificationUtc = null;
            _switchTimestamps.Clear();
        }

        _tracker.StartTracking();
        StartPollTimer();
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;
        }

        StopPollTimer();

        lock (_lock)
        {
            var nowUtc = DateTime.UtcNow;
            if (_distractingSegmentStartUtc.HasValue)
            {
                _distractingSeconds += (nowUtc - _distractingSegmentStartUtc.Value).TotalSeconds;
                _distractingSegmentStartUtc = null;
            }

            var totalSeconds = (nowUtc - _sessionStartTimeUtc).TotalSeconds;
            if (totalSeconds <= 0)
            {
                InvokeSessionEnded(100, "Session too short to score.", 0, 0, 0);
                return;
            }

            var score = SessionScoreCalculator.Calculate(
                totalSeconds, _distractingSeconds, _distractionEventCount,
                _config.ScorePenaltyPerDistractionEvent);

            var summaryParts = new List<string>();
            if (_distractionEventCount > 0)
                summaryParts.Add($"{_distractionEventCount} distraction(s)");
            if (_distractingSeconds > 0)
                summaryParts.Add($"{TimeSpan.FromSeconds(_distractingSeconds).TotalMinutes:F0} min in distracting apps");
            var summary = summaryParts.Count > 0 ? string.Join("; ", summaryParts) : "Stayed focused.";

            InvokeSessionEnded(score, summary, (long)totalSeconds, (long)_distractingSeconds, _distractionEventCount);
        }
    }

    private void StartPollTimer()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        _pollTimer = dispatcher.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(2.5);
        _pollTimer.Tick += (_, _) => Poll();
        _pollTimer.Start();
    }

    private void StopPollTimer()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void Poll()
    {
        if (!IsRunning) return;

        var app = _tracker.GetCurrentAppName();
        var category = _tracker.GetCurrentCategory();

        lock (_lock)
        {
            if (!_isRunning) return;

            var nowUtc = DateTime.UtcNow;
            var isSwitch = app != _lastApp || category != _lastCategory;

            if (isSwitch)
            {
                _switchTimestamps.Add(nowUtc);
                TrimSwitchesToWindow(nowUtc);
                Debug.WriteLine($"[SessionDistraction] App/category switch: {_lastApp} -> {app}, category {_lastCategory} -> {category}, switches in window={_switchTimestamps.Count}");
                _lastApp = app;
                _lastCategory = category;
            }

            var isDistractingCategory = _config.DistractingCategories.Contains(category);

            if (isDistractingCategory)
            {
                if (!_distractingSegmentStartUtc.HasValue)
                    _distractingSegmentStartUtc = nowUtc;

                DistractionEvent? toNotify = null;
                if (CanSendNotification(nowUtc))
                {
                    _distractionEventCount++;
                    _lastNotificationUtc = nowUtc;
                    toNotify = new DistractionEvent("distracting_category", _activityName, category);
                    Debug.WriteLine($"[SessionDistraction] Triggering notification: distracting_category (category={category}, activity={_activityName})");
                }
                if (toNotify != null)
                {
                    _lastApp = app;
                    _lastCategory = category;
                    NotifyDistraction(toNotify);
                    return;
                }
            }
            else
            {
                if (_distractingSegmentStartUtc.HasValue)
                {
                    _distractingSeconds += (nowUtc - _distractingSegmentStartUtc.Value).TotalSeconds;
                    _distractingSegmentStartUtc = null;
                }
            }

            var switchCountInWindow = _switchTimestamps.Count;
            DistractionEvent? freqEvt = null;
            if (switchCountInWindow >= _config.FrequentSwitchThreshold && CanSendNotification(nowUtc))
            {
                _distractionEventCount++;
                _lastNotificationUtc = nowUtc;
                freqEvt = new DistractionEvent("frequent_switching", _activityName, $"{switchCountInWindow} switches");
                Debug.WriteLine($"[SessionDistraction] Triggering notification: frequent_switching (switches={switchCountInWindow}, threshold={_config.FrequentSwitchThreshold}, activity={_activityName})");
            }
            if (freqEvt != null)
                NotifyDistraction(freqEvt);
        }
    }

    private void TrimSwitchesToWindow(DateTime nowUtc)
    {
        var cutoff = nowUtc.AddMinutes(-_config.FrequentSwitchWindowMinutes);
        while (_switchTimestamps.Count > 0 && _switchTimestamps[0] < cutoff)
            _switchTimestamps.RemoveAt(0);
    }

    private bool CanSendNotification(DateTime nowUtc)
    {
        if (!_lastNotificationUtc.HasValue) return true;
        var elapsed = (nowUtc - _lastNotificationUtc.Value).TotalMinutes;
        var allowed = elapsed >= _config.NotificationDebounceMinutes;
        if (!allowed)
            Debug.WriteLine($"[SessionDistraction] Notification debounced: {elapsed:F1} min since last (need {_config.NotificationDebounceMinutes} min)");
        return allowed;
    }

    private void NotifyDistraction(DistractionEvent evt)
    {
        DistractionDetected?.Invoke(evt);
        var title = "You seem distracted";
        var body = string.IsNullOrEmpty(_activityName)
            ? "Get back to your focus session."
            : $"Get back to {_activityName}.";
        Debug.WriteLine($"[SessionDistraction] Sending notification: title=\"{title}\", body=\"{body}\", reason={evt.Reason}");
        _notificationManager.SendNotification(title, body);
    }

    private void InvokeSessionEnded(int score, string summary, long totalSeconds, long distractingSeconds, int distractionEventCount)
    {
        var result = new SessionScoreResult(score, summary, totalSeconds, distractingSeconds, distractionEventCount);
        if (Application.Current?.Dispatcher != null)
            Application.Current.Dispatcher.Dispatch(() => SessionEndedWithScore?.Invoke(result));
        else
            SessionEndedWithScore?.Invoke(result);
    }
}
