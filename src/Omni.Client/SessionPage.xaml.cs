using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Dispatching;
using Omni.Client.Abstractions;
using Omni.Client.Models.Session;

namespace Omni.Client;

public partial class SessionPage : ContentPage, INotifyPropertyChanged
{
    private ISessionService? _sessionService;
    private IRunningSessionState? _runningState;
    private ISessionDistractionService? _distractionService;
    private bool _isRefreshing;
    private string _emptyView = "Pull to load session history.";
    private ObservableCollection<SessionDateGroup> _groupedEntries = new();
    private readonly List<SessionSyncEntry> _pendingSessions = new();
    private readonly string[] _activityTypes = { "work", "break", "other" };
    private readonly (string Label, int Seconds)[] _durationPresets =
    {
        ("15 min", 15 * 60),
        ("25 min", 25 * 60),
        ("30 min", 30 * 60),
        ("45 min", 45 * 60),
        ("1 h", 60 * 60),
        ("2 h", 2 * 60 * 60),
        ("Custom", -1),
        ("No limit", 0)
    };
    private const int CustomDurationPresetIndex = 6;
    private DateTime _sessionStartTime;
    private DateTime? _sessionEndTime;
    private bool _isRunning;

    private IRunningSessionState GetRunningState()
    {
        if (_runningState == null)
            _runningState = MauiProgram.AppServices?.GetService<IRunningSessionState>();
        return _runningState!;
    }

    private ISessionDistractionService? GetDistractionService()
    {
        _distractionService ??= MauiProgram.AppServices?.GetService<ISessionDistractionService>();
        return _distractionService;
    }

    public SessionPage()
    {
        InitializeComponent();
        BindingContext = this;
        RefreshCommand = new Command(async () => await LoadAsync());
        ActivityTypePicker.ItemsSource = _activityTypes;
        ActivityTypePicker.SelectedIndex = 0;
        DurationPicker.ItemsSource = _durationPresets.Select(p => p.Label).ToList();
        DurationPicker.SelectedIndex = 2; // 30 min default
    }

    private void OnDurationChanged(object? sender, EventArgs e)
    {
        var i = DurationPicker.SelectedIndex;
        CustomDurationSection.IsVisible = i == CustomDurationPresetIndex;
    }

    private ISessionService GetSessionService()
    {
        if (_sessionService == null)
            _sessionService = MauiProgram.AppServices?.GetService<ISessionService>();
        return _sessionService!;
    }

    public ICommand RefreshCommand { get; }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set { if (_isRefreshing != value) { _isRefreshing = value; OnPropertyChanged(); } }
    }

    public string EmptyView
    {
        get => _emptyView;
        set { if (_emptyView != value) { _emptyView = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<SessionDateGroup> GroupedEntries
    {
        get => _groupedEntries;
        set { _groupedEntries = value; OnPropertyChanged(); }
    }

    private SessionScoreResult? _lastScoreResult;
    public SessionScoreResult? LastScoreResult
    {
        get => _lastScoreResult;
        set { _lastScoreResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPostSessionSheetVisible)); OnPropertyChanged(nameof(ScoreLabel)); OnPropertyChanged(nameof(ScoreSummaryLabel)); }
    }

    public bool IsPostSessionSheetVisible => LastScoreResult != null;
    public string ScoreLabel => LastScoreResult != null ? $"{LastScoreResult.Score}/100" : "";
    public string ScoreSummaryLabel => LastScoreResult?.Summary ?? "";

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var auth = MauiProgram.AppServices?.GetService<IAuthService>();
        if (auth != null && !await auth.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }
        var state = GetRunningState();
        var distraction = GetDistractionService();
        if (distraction != null)
        {
            distraction.SessionEndedWithScore -= OnSessionEndedWithScore;
            distraction.SessionEndedWithScore += OnSessionEndedWithScore;
        }
        if (state != null)
        {
            state.Tick -= OnRunningStateTick;
            state.CountdownReachedZero -= OnCountdownReachedZeroFromState;
            state.Tick += OnRunningStateTick;
            state.CountdownReachedZero += OnCountdownReachedZeroFromState;
            if (state.IsRunning)
            {
                _sessionStartTime = state.StartTimeUtc;
                _sessionEndTime = state.EndTimeUtc;
                _isRunning = true;
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                ActivityNameEntry.IsEnabled = false;
                ActivityTypePicker.IsEnabled = false;
                DurationPicker.IsEnabled = false;
                CustomDurationEntry.IsEnabled = false;
                UpdateTimerDisplayFromState(state);
            }
        }
        _ = LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        var distraction = GetDistractionService();
        if (distraction != null)
            distraction.SessionEndedWithScore -= OnSessionEndedWithScore;
        var state = GetRunningState();
        if (state != null)
        {
            state.Tick -= OnRunningStateTick;
            state.CountdownReachedZero -= OnCountdownReachedZeroFromState;
        }
    }

    private void OnRunningStateTick(int? remainingSeconds)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var state = GetRunningState();
            if (state == null || !state.IsRunning) return;
            UpdateTimerDisplayFromState(state);
        });
    }

    private void UpdateTimerDisplayFromState(IRunningSessionState state)
    {
        var remaining = state.GetRemainingSeconds();
        if (remaining.HasValue)
        {
            if (remaining.Value <= 0)
                return;
            var total = remaining.Value;
            var h = total / 3600;
            var m = (total % 3600) / 60;
            var sec = total % 60;
            TimerLabel.Text = h > 0 ? $"{h}:{m:D2}:{sec:D2}" : $"{m:D2}:{sec:D2}";
        }
        else
        {
            var elapsed = state.GetElapsedSeconds();
            var h = elapsed / 3600;
            var m = (elapsed % 3600) / 60;
            var s = elapsed % 60;
            TimerLabel.Text = $"{(int)h:D2}:{m:D2}:{s:D2}";
        }
    }

    private async void OnCountdownReachedZeroFromState()
    {
        var state = GetRunningState();
        if (state == null || !state.IsRunning) return;
        _isRunning = false;
        await SaveAndStopSessionAsync(state.ActivityName, state.ActivityType, state.StartTimeUtc);
        state.Stop();
    }

    private void OnActivityTypeChanged(object? sender, EventArgs e)
    {
        // No-op; selected type read on start/stop
    }

    private void OnStartClicked(object? sender, EventArgs e)
    {
        var name = (ActivityNameEntry.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            ActivityNameEntry.Placeholder = "Enter a name (e.g. Work, Focus)";
            return;
        }
        var durationSeconds = GetSelectedDurationSeconds();
        if (IsCustomDurationSelected && durationSeconds <= 0)
        {
            CustomDurationEntry.Placeholder = "Enter minutes (e.g. 90)";
            return;
        }
        var typeIndex = ActivityTypePicker.SelectedIndex;
        var activityType = typeIndex >= 0 && typeIndex < _activityTypes.Length ? _activityTypes[typeIndex] : "other";

        var state = GetRunningState();
        if (state == null) return;

        state.Start(name, activityType, durationSeconds > 0 ? durationSeconds : null);

        _sessionStartTime = state.StartTimeUtc;
        _sessionEndTime = state.EndTimeUtc;
        _isRunning = true;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ActivityNameEntry.IsEnabled = false;
        ActivityTypePicker.IsEnabled = false;
        DurationPicker.IsEnabled = false;
        CustomDurationEntry.IsEnabled = false;
        UpdateTimerDisplayFromState(state);
    }

    private int GetSelectedDurationSeconds()
    {
        var i = DurationPicker.SelectedIndex;
        if (i < 0 || i >= _durationPresets.Length) return 0;
        var preset = _durationPresets[i].Seconds;
        if (preset >= 0)
            return preset;
        // Custom: parse minutes from entry
        var text = (CustomDurationEntry.Text ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return 0;
        if (!int.TryParse(text, out var minutes) || minutes <= 0) return 0;
        const int maxMinutes = 24 * 60;
        if (minutes > maxMinutes) minutes = maxMinutes;
        return minutes * 60;
    }

    private bool IsCustomDurationSelected => DurationPicker.SelectedIndex == CustomDurationPresetIndex;

    private async void OnStopClicked(object? sender, EventArgs e)
    {
        if (!_isRunning) return;
        var state = GetRunningState();
        if (state == null) return;
        _isRunning = false;
        await SaveAndStopSessionAsync(state.ActivityName, state.ActivityType, state.StartTimeUtc);
        state.Stop();
    }

    private async Task SaveAndStopSessionAsync(string activityName, string activityType, DateTime startedAtUtc)
    {
        var endTime = DateTime.UtcNow;
        var durationSeconds = (long)(endTime - startedAtUtc).TotalSeconds;
        if (durationSeconds <= 0)
        {
            ResetTimerControls();
            return;
        }

        var name = string.IsNullOrEmpty(activityName) ? "Activity" : activityName;

        var entry = new SessionSyncEntry
        {
            Name = name,
            ActivityType = activityType,
            StartedAt = startedAtUtc.ToString("O"),
            DurationSeconds = durationSeconds
        };
        _pendingSessions.Add(entry);

        ResetTimerControls();
        _isRunning = false;
        TimerLabel.Text = "00:00";

        var sessionService = GetSessionService();
        if (sessionService != null)
        {
            var success = await sessionService.SyncSessionsAsync(_pendingSessions);
            if (success)
                _pendingSessions.Clear();
        }
        await LoadAsync();
    }

    private void OnSessionEndedWithScore(SessionScoreResult result)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LastScoreResult = result;
        });
    }

    private void OnPostSessionDoneClicked(object? sender, EventArgs e)
    {
        LastScoreResult = null;
    }

    private void ResetTimerControls()
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        ActivityNameEntry.IsEnabled = true;
        ActivityTypePicker.IsEnabled = true;
        DurationPicker.IsEnabled = true;
        CustomDurationEntry.IsEnabled = true;
    }

    private async Task LoadAsync()
    {
        IsRefreshing = true;
        try
        {
            var sessionService = GetSessionService();
            if (sessionService == null)
            {
                EmptyView = "Service not available.";
                GroupedEntries = new ObservableCollection<SessionDateGroup>();
                return;
            }

            var from = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");
            var to = DateTime.Now.ToString("yyyy-MM-dd");
            var response = await sessionService.GetSessionsAsync(from, to);
            if (response == null)
            {
                EmptyView = "Not signed in or couldn't load sessions.";
                GroupedEntries = new ObservableCollection<SessionDateGroup>();
                return;
            }

            var entries = response.Entries ?? new List<SessionListEntry>();
            var byDate = entries
                .Select(e => (Entry: e, Date: DateTime.TryParse(e.StartedAt, out var dt) ? dt.ToString("yyyy-MM-dd") : ""))
                .Where(x => !string.IsNullOrEmpty(x.Date))
                .GroupBy(x => x.Date)
                .OrderByDescending(g => g.Key)
                .Select(g => new SessionDateGroup(g.Key, g.Select(x => SessionDisplayItem.FromEntry(x.Entry)).ToList()))
                .ToList();

            GroupedEntries = new ObservableCollection<SessionDateGroup>(byDate);
            EmptyView = GroupedEntries.Count == 0 ? "No sessions yet. Start a session above." : "Pull to refresh.";
        }
        catch (Exception ex)
        {
            EmptyView = "Error loading sessions.";
            GroupedEntries = new ObservableCollection<SessionDateGroup>();
            System.Diagnostics.Debug.WriteLine($"SessionPage LoadAsync: {ex}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
