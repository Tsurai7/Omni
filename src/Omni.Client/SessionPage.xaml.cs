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
    private IDispatcherTimer? _timer;
    private bool _isRunning;

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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
        _timer = null;
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
        _sessionStartTime = DateTime.UtcNow;
        _sessionEndTime = durationSeconds > 0 ? _sessionStartTime.AddSeconds(durationSeconds) : null;

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ActivityNameEntry.IsEnabled = false;
        ActivityTypePicker.IsEnabled = false;
        DurationPicker.IsEnabled = false;
        CustomDurationEntry.IsEnabled = false;
        _isRunning = true;
        _timer = Application.Current?.Dispatcher.CreateTimer();
        if (_timer != null)
        {
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) =>
            {
                if (!_isRunning) return;
                MainThread.BeginInvokeOnMainThread(UpdateTimerDisplay);
            };
            _timer.Start();
        }
        UpdateTimerDisplay();
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

    private void UpdateTimerDisplay()
    {
        if (_sessionEndTime.HasValue)
        {
            var remaining = _sessionEndTime.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                MainThread.BeginInvokeOnMainThread(() => _ = OnCountdownReachedZeroAsync());
                return;
            }
            var total = (int)remaining.TotalSeconds;
            var h = total / 3600;
            var m = (total % 3600) / 60;
            var sec = total % 60;
            TimerLabel.Text = h > 0 ? $"{h}:{m:D2}:{sec:D2}" : $"{m:D2}:{sec:D2}";
        }
        else
        {
            var elapsed = DateTime.UtcNow - _sessionStartTime;
            TimerLabel.Text = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
    }

    private async Task OnCountdownReachedZeroAsync()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _timer?.Stop();
        _timer = null;
        await SaveAndStopSessionAsync();
    }

    private async void OnStopClicked(object? sender, EventArgs e)
    {
        if (!_isRunning) return;
        _isRunning = false;
        _timer?.Stop();
        _timer = null;
        await SaveAndStopSessionAsync();
    }

    private async Task SaveAndStopSessionAsync()
    {
        var endTime = DateTime.UtcNow;
        var durationSeconds = (long)(endTime - _sessionStartTime).TotalSeconds;
        if (durationSeconds <= 0)
        {
            ResetTimerControls();
            return;
        }

        var name = (ActivityNameEntry.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name)) name = "Activity";
        var typeIndex = ActivityTypePicker.SelectedIndex;
        var activityType = typeIndex >= 0 && typeIndex < _activityTypes.Length ? _activityTypes[typeIndex] : "other";

        var entry = new SessionSyncEntry
        {
            Name = name,
            ActivityType = activityType,
            StartedAt = _sessionStartTime.ToString("O"),
            DurationSeconds = durationSeconds
        };
        _pendingSessions.Add(entry);

        ResetTimerControls();
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
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
