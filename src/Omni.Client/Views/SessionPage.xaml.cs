using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.Dispatching;
using Omni.Client.Abstractions;
using Omni.Client.Controls;
using Omni.Client.Models.Session;
using Omni.Client.Presentation.ViewModels;

namespace Omni.Client;

public partial class SessionPage : ContentPage
{
    private readonly SessionViewModel _vm;
    private IRunningSessionState? _runningState;
    private ISessionDistractionService? _distractionService;
    private bool _navigatedFromChat;

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
    private int _selectedDurationIndex = 1;
    private const int CustomDurationPresetIndex = 6;

    private readonly BreathingCircleDrawable _breathingDrawable = new();
    private IDispatcherTimer? _breathingTimer;
    private double _breathingPhase;
    private int _breathingSecondsLeft = 30;
    private int _breathingTickCount;

    private IDispatcherTimer? _recordingTimer;
    private bool _recordingDotVisible = true;

    private int _subjectiveRating;

    private IRunningSessionState GetRunningState() =>
        _runningState ??= MauiProgram.AppServices?.GetService<IRunningSessionState>()!;

    private ISessionDistractionService? GetDistractionService() =>
        _distractionService ??= MauiProgram.AppServices?.GetService<ISessionDistractionService>();

    public SessionPage(SessionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        ActivityTypePicker.ItemsSource = _activityTypes;
        ActivityTypePicker.SelectedIndex = 0;

        BuildDurationPills();

        BreathingView.Drawable = _breathingDrawable;
    }

    internal void SetNavigatedFromChat()
    {
        _navigatedFromChat = true;
    }

    private async void OnBackToCoachTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("///ChatPage");
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
        for (int i = 0; i < DurationPillsLayout.Children.Count; i++)
        {
            if (DurationPillsLayout.Children[i] is Button b)
            {
                b.BackgroundColor = i == index ? Color.FromArgb("#4ECCA3") : Color.FromArgb("#222228");
                b.TextColor       = i == index ? Color.FromArgb("#0F1210") : Color.FromArgb("#4ECCA3");
            }
        }
        CustomDurationSection.IsVisible = index == CustomDurationPresetIndex;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        BackToCoachButton.IsVisible = _navigatedFromChat;
        _navigatedFromChat = false;

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
                ActiveActivityLabel.Text = state.ActivityName;
                TransitionTo(SessionFlowState.Active);
                StartRecordingBlink();
                UpdateTimerDisplayFromState(state);
            }
        }
        _ = _vm.LoadAsync();
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

    private void TransitionTo(SessionFlowState state)
    {
        IntentionPanel.IsVisible   = state == SessionFlowState.Intention;
        BreathingPanel.IsVisible   = state == SessionFlowState.Breathing;
        ActivePanel.IsVisible      = state == SessionFlowState.Active;
        PostSessionOverlay.IsVisible = state == SessionFlowState.PostSession;
    }

    private void OnBeginBreathingClicked(object? sender, EventArgs e)
    {
        var name = (ActivityNameEntry.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            ActivityNameEntry.Placeholder = "Please enter what you're working on";
            _ = ActivityNameEntry.ShakePlaceholderAsync();
            return;
        }
        if (_selectedDurationIndex == CustomDurationPresetIndex && GetSelectedDurationSeconds() <= 0)
        {
            CustomDurationEntry.Placeholder = "Enter minutes";
            return;
        }
        TransitionTo(SessionFlowState.Breathing);
        StartBreathing();
    }

    private void StartBreathing()
    {
        _breathingSecondsLeft = 30;
        _breathingPhase = 0;
        _breathingTickCount = 0;
        BreathingCountLabel.Text = _breathingSecondsLeft.ToString();

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

    private void OnBreathingTick(object? sender, EventArgs e)
    {
        _breathingPhase += 50.0 / 6000.0;
        if (_breathingPhase >= 1.0) _breathingPhase -= 1.0;
        _breathingDrawable.SetPhase(_breathingPhase);
        BreathingView.Invalidate();

        _breathingTickCount++;
        if (_breathingTickCount % 20 == 0)
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

    private void StartSession()
    {
        var name          = (ActivityNameEntry.Text ?? "").Trim();
        var durationSec   = GetSelectedDurationSeconds();
        var typeIndex     = ActivityTypePicker.SelectedIndex;
        var activityType  = typeIndex >= 0 && typeIndex < _activityTypes.Length ? _activityTypes[typeIndex] : "other";

        var state = GetRunningState();
        if (state == null) return;

        state.Start(name, activityType, durationSec > 0 ? durationSec : null);
        GetDistractionService()?.Start(state.StartTimeUtc, name);

        ActiveActivityLabel.Text = string.IsNullOrEmpty(name) ? "Focus session" : name;
        TransitionTo(SessionFlowState.Active);
        StartRecordingBlink();
        UpdateTimerDisplayFromState(state);
    }

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
            var h   = remaining.Value / 3600;
            var m   = (remaining.Value % 3600) / 60;
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
        GetDistractionService()?.Stop();
        await SaveAndStopSessionAsync(state.ActivityName, state.ActivityType, state.StartTimeUtc);
        state.Stop();
    }

    private async void OnStopClicked(object? sender, EventArgs e)
    {
        var state = GetRunningState();
        if (state == null || !state.IsRunning) return;
        GetDistractionService()?.Stop();
        await SaveAndStopSessionAsync(state.ActivityName, state.ActivityType, state.StartTimeUtc);
        state.Stop();
    }

    private async Task SaveAndStopSessionAsync(string activityName, string activityType, DateTime startedAtUtc)
    {
        _recordingTimer?.Stop();
        var endTime       = DateTime.UtcNow;
        var durationSec   = (long)(endTime - startedAtUtc).TotalSeconds;

        if (durationSec <= 0)
        {
            TransitionTo(SessionFlowState.Intention);
            return;
        }

        var entry = new SessionSyncEntry
        {
            Name          = string.IsNullOrEmpty(activityName) ? "Activity" : activityName,
            ActivityType  = activityType,
            StartedAt     = startedAtUtc.ToString("O"),
            DurationSeconds = durationSec
        };

        await _vm.LoadAsync();
        TransitionTo(SessionFlowState.Intention);
        ActivityNameEntry.Text = "";
    }

    private void OnSessionEndedWithScore(SessionScoreResult result)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PostScoreLabel.Text   = result.Score.ToString();
            PostSummaryLabel.Text = result.Summary;
            TransitionTo(SessionFlowState.PostSession);
        });
    }

    private void OnDistractionDetected(DistractionEvent evt)
    {
        var label = !string.IsNullOrWhiteSpace(evt.CategoryOrDetail)
            ? evt.CategoryOrDetail
            : !string.IsNullOrWhiteSpace(evt.ActivityName)
                ? evt.ActivityName
                : "a distracting app";
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DistractionBannerLabel.Text = $"You switched to {label} — stay focused?";
            DistractionBanner.IsVisible = true;
            _ = Task.Delay(8000).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() => DistractionBanner.IsVisible = false));
        });
    }

    private void OnEmojiRatingClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string ratingStr
            && int.TryParse(ratingStr, out var rating))
        {
            _subjectiveRating = rating;
            foreach (var child in EmojiRatingRow.Children.OfType<Button>())
                child.Opacity = child == btn ? 1.0 : 0.4;
        }
    }

    private void OnPostSessionDoneClicked(object? sender, EventArgs e)
    {
        _subjectiveRating = 0;
        ReflectionEntry.Text = "";
        foreach (var child in EmojiRatingRow.Children.OfType<Button>())
            child.Opacity = 1.0;
        TransitionTo(SessionFlowState.Intention);
    }

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
}

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
