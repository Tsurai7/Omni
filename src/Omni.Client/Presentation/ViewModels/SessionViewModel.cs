using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Omni.Client.Abstractions;
using Omni.Client.Models.Session;

namespace Omni.Client.Presentation.ViewModels;

public enum SessionFlowState { Intention, Breathing, Active, PostSession }

public partial class SessionViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;
    private readonly IRunningSessionState _runningState;
    private readonly ISessionDistractionService? _distractionService;

    private DateTime _sessionStartTime;
    private int _subjectiveRating;
    private readonly List<SessionSyncEntry> _pendingSessions = new();

    [ObservableProperty]
    private SessionFlowState _flowState = SessionFlowState.Intention;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _emptyView = "No sessions yet. Start one above.";

    [ObservableProperty]
    private ObservableCollection<SessionDateGroup> _groupedEntries = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private int _subjectiveRatingValue;

    public event Action? SessionStarted;
    public event Action? SessionEnded;

    public SessionViewModel(
        ISessionService sessionService,
        IRunningSessionState runningState,
        ISessionDistractionService? distractionService = null)
    {
        _sessionService = sessionService;
        _runningState = runningState;
        _distractionService = distractionService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var to   = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
            var from = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd");
            var response = await _sessionService.GetSessionsAsync(from, to);
            if (response?.Entries == null || response.Entries.Count == 0)
            {
                GroupedEntries = new ObservableCollection<SessionDateGroup>();
                return;
            }

            var groups = response.Entries
                .GroupBy(s =>
                {
                    if (DateTime.TryParse(s.StartedAt, out var dt)) return dt.Date.ToString("yyyy-MM-dd");
                    return DateTime.Today.ToString("yyyy-MM-dd");
                })
                .OrderByDescending(g => g.Key)
                .Select(g => new SessionDateGroup(
                    g.Key,
                    g.Select(s => SessionDisplayItem.FromEntry(s)).ToList()))
                .ToList();

            GroupedEntries = new ObservableCollection<SessionDateGroup>(groups);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SessionViewModel.LoadAsync: {ex.Message}");
        }
    }

    public void StartSession(string activityType, int durationSeconds)
    {
        _sessionStartTime = DateTime.UtcNow;
        IsRunning = true;
        _runningState.Start(activityType, activityType, durationSeconds);
        _distractionService?.Start(_sessionStartTime, activityType);
        FlowState = SessionFlowState.Breathing;
        SessionStarted?.Invoke();
    }

    public void TransitionToActive()
        => FlowState = SessionFlowState.Active;

    public async Task EndSessionAsync(string activityType, int distractionCount, string intention, string reflection)
    {
        var now = DateTime.UtcNow;
        _runningState.Stop();
        _distractionService?.Stop();
        IsRunning = false;

        var entry = new SessionSyncEntry(
            Name: activityType,
            ActivityType: activityType,
            StartedAt: _sessionStartTime.ToString("O"),
            DurationSeconds: (int)(now - _sessionStartTime).TotalSeconds,
            GoalId: null,
            GoalTargetMinutes: null,
            SessionScore: null,
            DistractionEventCount: distractionCount);
        _pendingSessions.Add(entry);

        try
        {
            await _sessionService.SyncSessionsAsync(_pendingSessions);
            _pendingSessions.Clear();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SessionViewModel.EndSession sync: {ex.Message}");
        }

        FlowState = SessionFlowState.PostSession;
        SessionEnded?.Invoke();
    }

    public void SetRating(int rating) => _subjectiveRating = rating;

    public void ResetFlow()
    {
        FlowState = SessionFlowState.Intention;
        _subjectiveRating = 0;
    }
}
