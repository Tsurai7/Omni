using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Dispatching;
using Omni.Client.Abstractions;
using Omni.Client.Controls;
using Omni.Client.Models.Session;

namespace Omni.Client;

public enum SessionFlowState { Intention, Breathing, Active, PostSession }

public partial class SessionPage : ContentPage, INotifyPropertyChanged
{
    private ISessionService? _sessionService;
    private IRunningSessionState? _runningState;
    private ISessionDistractionService? _distractionService;
    private bool _isRefreshing;
    private string _emptyView = "No sessions yet. Start one above.";
    private ObservableCollection<SessionDateGroup> _groupedEntries = new();
    private readonly List<SessionSyncEntry> _pendingSessions = new();
    private readonly string[] _activityTypes = { "work", "break", "other" };
    private readonly (string Label, int Seconds)[] _durationPresets =
    {
        ("15m", 15 * 60),
        ("25m", 25 * 60),
        ("30m", 30 * 60),
        ("45m", 45 * 60),
        ("1h",  60 * 60),
        ("2h",  2 * 60 * 60),
        ("Custom", -1),
        ("No limit", 0)
    };
    private int _selectedDurationIndex = 1; // 25m default
    private const int CustomDurationPresetIndex = 6;
    private DateTime _sessionStartTime;
    private bool _isRunning;

    // Breathing animation
    private readonly BreathingCircleDrawable _breathingDrawable = new();
    private IDispatcherTimer? _breathingTimer;
    private double _breathingPhase;
    private int _breathingSecondsLeft = 30;

    // Active session recording dot blink
    private IDispatcherTimer? _recordingTimer;
    private bool _recordingDotVisible = true;

    // Post-session
    private int _subjectiveRating;

    // Flow state
    private SessionFlowState _flowState = SessionFlowState.Intention;

    private IRunningSessionState GetRunningState() =>
        _runningState ??= MauiProgram.AppServices?.GetService<IRunningSessionState>()!;

    private ISessionDistractionService? GetDistractionService() =>
        _distractionService ??= MauiProgram.AppServices?.GetService<ISessionDistractionService>();

    public SessionPage()
    {
        InitializeComponent();
        BindingContext = this;
        RefreshCommand = new Command(async () => await LoadAsync());

        // Activity type picker
        ActivityTypePicker.ItemsSource = _activityTypes;
        ActivityTypePicker.SelectedIndex = 0;

        // Duration pills (built in code-behind for visual control)
        BuildDurationPills();

        // Breathing drawable
        BreathingView.Drawable = _breathingDrawable;
    }

    private void BuildDurationPills()
    {
        Style? pillStyle = null;
        try { pillStyle = (Style)Application.Current!.Resources["ProductivityPillButton"]; } catch { }

        for (int i = 0; i < _durationPresets.Length; i++)
        {
            var idx = i;
            var btn = new Button
            {
                Text = _durationPresets[i].Label,
                Style = pillStyle,
                BackgroundColor = i == _selectedDurationIndex
                    ? Color.FromArgb("#4ECCA3")
                    : Color.FromArgb("#222228"),
                TextColor = i == _selectedDurationIndex
                    ? Color.FromArgb("#0F1210")
                    : Color.FromArgb("#4ECCA3"),
            };
            btn.Clicked += (s, e) => OnDurationPillSelected(idx);
            DurationPillsLayout.Children.Add(btn);
        }
    }

    private void OnDurationPillSelected(int index)
    {
        _selectedDurationIndex = index;
        // Update pill styling
        for (int i = 0; i < DurationPillsLayout.Children.Count; i++)
        {
            if (DurationPillsLayout.Children[i] is Button b)
            {
                b.BackgroundColor = i == index
                    ? Color.FromArgb("#4ECCA3")
                    : Color.FromArgb("#222228");
                b.TextColor = i == index
                    ? Color.FromArgb("#0F1210")
                    : Color.FromArgb("#4ECCA3");
            }
        }
        CustomDurationSection.IsVisible = index == CustomDurationPresetIndex;
    }

    private ISessionService GetSessionService() =>
        _sessionService ??= MauiProgram.AppServices?.GetService<ISessionService>()!;

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
        set
        {
            _lastScoreResult = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPostSessionSheetVisible));
            OnPropertyChanged(nameof(ScoreLabel));
            OnPropertyChanged(nameof(ScoreSummaryLabel));
        }
    }

    public bool IsPostSessionSheetVisible => LastScoreResult != null;
    public string ScoreLabel => LastScoreResult != null ? $"{LastScoreResult.Score}/100" : "";
    public string ScoreSummaryLabel => LastScoreResult?.Summary ?? "";

    // ── Flow state helpers ────────────────────────────────────────────────────

    private void TransitionTo(SessionFlowState state)
    {
        _flowState = state;
        IntentionPanel.IsVisible = state == SessionFlowState.Intention;
        BreathingPanel.IsVisible = state == SessionFlowState.Breathing;
        ActivePanel.IsVisible = state == SessionFlowState.Active;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var auth = MauiProgram.AppServices?.GetService<IAuthService>();
        if (auth != null && !await auth.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }

        var distraction = GetDistractionService();
        if (distraction != null)
        {
            distraction.SessionEndedWithScore -= OnSessionEndedWithScore;
            distraction.SessionEndedWithScore += OnSessionEndedWithScore;
            distraction.DistractionDetected -= OnDistractionDetected;
            distraction.DistractionDetected += OnDistractionDetected;
        }

        var state = GetRunningState();
        if (state != null)
        {
            state.Tick -= OnRunningStateTick;
            state.CountdownReachedZero -= OnCountdownReachedZeroFromState;
            state.Tick += OnRunningStateTick;
            state.CountdownReachedZero += OnCountdownReachedZeroFromState;

            if (state.IsRunning)
            {
                _sessionStartTime = state.StartTimeUtc;
                _isRunning = true;
                ActiveActivityLabel.Text = state.ActivityName;
                TransitionTo(SessionFlowState.Active);
                StartRecordingBlink();
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
        {
            distraction.SessionEndedWithScore -= OnSessionEndedWithScore;
            distraction.DistractionDetected -= OnDistractionDetected;
        }
        var state = GetRunningState();
        if (state != null)
        {
            state.Tick -= OnRunningStateTick;
            state.CountdownReachedZero -= OnCountdownReachedZeroFromState;
        }
        StopBreathing();
        _recordingTimer?.Stop();
    }

    // ── Intention → Breathing ─────────────────────────────────────────────────

    private void OnBeginBreathingClicked(object? sender, EventArgs e)
    {
        var name = (ActivityNameEntry.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            ActivityNameEntry.Placeholder = "Please enter what you're working on";
            _ = ActivityNameEntry.ShakePlaceholderAsync();
            return;
        }
        if (_selectedDurationIndex == CustomDurationPresetIndex)
        {
            var customSec = GetSelectedDurationSeconds();
            if (customSec <= 0)
            {
                CustomDurationEntry.Placeholder = "Enter minutes";
                return;
            }
        }
        TransitionTo(SessionFlowState.Breathing);
        StartBreathing();
    }

    // ── Breathing ─────────────────────────────────────────────────────────────

    private void StartBreathing()
    {
        _breathingSecondsLeft = 30;
        _breathingPhase = 0;
        BreathingCountLabel.Text = _breathingSecondsLeft.ToString();
        BreathingView.Drawable = _breathingDrawable;

        _breathingTimer = Dispatcher.CreateTimer();
        _breathingTimer.Interval = TimeSpan.FromMilliseconds(50);
        _breathingTimer.Tick += OnBreathingTick;
        _breathingTimer.Start();
    }

    private void StopBreathing()
    {
        _breathingTimer?.Stop();
        _breathingTimer = null;
    }

    private int _breathingTickCount;
    private void OnBreathingTick(object? sender, EventArgs e)
    {
        _breathingPhase += 50.0 / 6000.0; // full cycle = 6s (3s inhale + 3s exhale)
        if (_breathingPhase >= 1.0) _breathingPhase -= 1.0;
        _breathingDrawable.SetPhase(_breathingPhase);
        BreathingView.Invalidate();

        _breathingTickCount++;
        if (_breathingTickCount % 20 == 0) // every ~1s
        {
            _breathingSecondsLeft--;
            BreathingCountLabel.Text = _breathingSecondsLeft.ToString();
            if (_breathingSecondsLeft <= 0)
            {
                StopBreathing();
                StartSession();
            }
        }
    }

    private void OnSkipBreathingClicked(object? sender, EventArgs e)
    {
        StopBreathing();
        StartSession();
    }

    // ── Start active session ──────────────────────────────────────────────────

    private void StartSession()
    {
        var name = (ActivityNameEntry.Text ?? "").Trim();
        var durationSeconds = GetSelectedDurationSeconds();
        var typeIndex = ActivityTypePicker.SelectedIndex;
        var activityType = typeIndex >= 0 && typeIndex < _activityTypes.Length ? _activityTypes[typeIndex] : "other";

        var state = GetRunningState();
        if (state == null) return;

        state.Start(name, activityType, durationSeconds > 0 ? durationSeconds : null);
        _sessionStartTime = state.StartTimeUtc;
        _isRunning = true;

        GetDistractionService()?.Start(state.StartTimeUtc, name);

        ActiveActivityLabel.Text = string.IsNullOrEmpty(name) ? "Focus session" : name;
        TransitionTo(SessionFlowState.Active);
        StartRecordingBlink();
        UpdateTimerDisplayFromState(state);
    }

    // ── Active session ────────────────────────────────────────────────────────

    private void StartRecordingBlink()
    {
        _recordingTimer = Dispatcher.CreateTimer();
        _recordingTimer.Interval = TimeSpan.FromMilliseconds(800);
        _recordingTimer.Tick += (_, _) =>
        {
            _recordingDotVisible = !_recordingDotVisible;
            RecordingDot.Opacity = _recordingDotVisible ? 1 : 0;
        };
        _recordingTimer.Start();
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
            if (remaining.Value <= 0) return;
            var h = remaining.Value / 3600;
            var m = (remaining.Value % 3600) / 60;
            var sec = remaining.Value % 60;
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
        GetDistractionService()?.Stop();
        await SaveAndStopSessionAsync(state.ActivityName, state.ActivityType, state.StartTimeUtc);
        state.Stop();
    }

    private async void OnStopClicked(object? sender, EventArgs e)
    {
        if (!_isRunning) return;
        var state = GetRunningState();
        if (state == null) return;
        _isRunning = false;
        GetDistractionService()?.Stop();
        await SaveAndStopSessionAsync(state.ActivityName, state.ActivityType, state.StartTimeUtc);
        state.Stop();
    }

    private async Task SaveAndStopSessionAsync(string activityName, string activityType, DateTime startedAtUtc)
    {
        _recordingTimer?.Stop();
        var endTime = DateTime.UtcNow;
        var durationSeconds = (long)(endTime - startedAtUtc).TotalSeconds;

        if (durationSeconds <= 0)
        {
            TransitionTo(SessionFlowState.Intention);
            return;
        }

        var entry = new SessionSyncEntry
        {
            Name = string.IsNullOrEmpty(activityName) ? "Activity" : activityName,
            ActivityType = activityType,
            StartedAt = startedAtUtc.ToString("O"),
            DurationSeconds = durationSeconds
        };
        _pendingSessions.Add(entry);

        var sessionService = GetSessionService();
        if (sessionService != null)
        {
            var success = await sessionService.SyncSessionsAsync(_pendingSessions);
            if (success) _pendingSessions.Clear();
        }
        await LoadAsync();
        TransitionTo(SessionFlowState.Intention);
        ActivityNameEntry.Text = "";
    }

    private void OnSessionEndedWithScore(SessionScoreResult result)
    {
        MainThread.BeginInvokeOnMainThread(() => { LastScoreResult = result; });
    }

    // ── Distraction hint ──────────────────────────────────────────────────────

    private void OnDistractionDetected(DistractionEvent evt)
    {
        var label = !string.IsNullOrWhiteSpace(evt.CategoryOrDetail)
            ? evt.CategoryOrDetail
            : !string.IsNullOrWhiteSpace(evt.ActivityName)
                ? evt.ActivityName
                : "a distracting app";
        MainThread.BeginInvokeOnMainThread(() => ShowDistractionHint(label));
    }

    public void ShowDistractionHint(string category)
    {
        if (_flowState != SessionFlowState.Active) return;
        DistractionBannerLabel.Text = $"You switched to {category} — stay focused?";
        DistractionBanner.IsVisible = true;
        _ = Task.Delay(8000).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() => DistractionBanner.IsVisible = false));
    }

    // ── Post-session ──────────────────────────────────────────────────────────

    private void OnEmojiRatingClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string ratingStr
            && int.TryParse(ratingStr, out var rating))
        {
            _subjectiveRating = rating;
            // Highlight selected emoji
            foreach (var child in EmojiRatingRow.Children.OfType<Button>())
                child.Opacity = child == btn ? 1.0 : 0.4;
        }
    }

    private void OnPostSessionDoneClicked(object? sender, EventArgs e)
    {
        LastScoreResult = null;
        _subjectiveRating = 0;
        ReflectionEntry.Text = "";
        foreach (var child in EmojiRatingRow.Children.OfType<Button>())
            child.Opacity = 1.0;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private int GetSelectedDurationSeconds()
    {
        var i = _selectedDurationIndex;
        if (i < 0 || i >= _durationPresets.Length) return 0;
        var preset = _durationPresets[i].Seconds;
        if (preset >= 0) return preset;
        var text = (CustomDurationEntry.Text ?? "").Trim();
        if (!int.TryParse(text, out var minutes) || minutes <= 0) return 0;
        return Math.Min(minutes, 24 * 60) * 60;
    }

    private async Task LoadAsync()
    {
        IsRefreshing = true;
        try
        {
            var sessionService = GetSessionService();
            if (sessionService == null) { EmptyView = "Service not available."; GroupedEntries = new(); return; }

            var from = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");
            var to = DateTime.Now.ToString("yyyy-MM-dd");
            var response = await sessionService.GetSessionsAsync(from, to);
            if (response == null) { EmptyView = "Couldn't load sessions."; GroupedEntries = new(); return; }

            var entries = response.Entries ?? new List<SessionListEntry>();
            var byDate = entries
                .Select(e => (Entry: e, Date: DateTime.TryParse(e.StartedAt, out var dt) ? dt.ToString("yyyy-MM-dd") : ""))
                .Where(x => !string.IsNullOrEmpty(x.Date))
                .GroupBy(x => x.Date)
                .OrderByDescending(g => g.Key)
                .Select(g => new SessionDateGroup(g.Key, g.Select(x => SessionDisplayItem.FromEntry(x.Entry)).ToList()))
                .ToList();

            GroupedEntries = new ObservableCollection<SessionDateGroup>(byDate);
            EmptyView = GroupedEntries.Count == 0 ? "No sessions yet. Start one above." : "Pull to refresh.";
        }
        catch (Exception ex)
        {
            EmptyView = "Error loading sessions.";
            GroupedEntries = new();
            System.Diagnostics.Debug.WriteLine($"SessionPage LoadAsync: {ex}");
        }
        finally { IsRefreshing = false; }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Extension helper for placeholder shake animation
internal static class EntryExtensions
{
    public static async Task ShakePlaceholderAsync(this Entry entry)
    {
        for (int i = 0; i < 2; i++)
        {
            await entry.TranslateToAsync(-6, 0, 50);
            await entry.TranslateToAsync(6, 0, 50);
        }
        await entry.TranslateToAsync(0, 0, 50);
    }
}
