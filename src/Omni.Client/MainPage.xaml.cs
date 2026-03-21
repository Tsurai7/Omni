using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Omni.Client.Abstractions;
using Omni.Client.Controls;
using Omni.Client.Models;
using Omni.Client.Services;

namespace Omni.Client;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    private readonly IActiveWindowTracker _tracker;
    private readonly IAuthService _authService;
    private readonly IUsageService _usageService;
    private readonly IProductivityService _productivityService;
    private readonly IFocusScoreService _focusScoreService;
    private readonly System.Timers.Timer _uiTimer;
    private const int UiUpdateIntervalMs = 2500;
    private bool _updateInProgress;

    private string? _nextBestActionFromServer;
    private string? _currentNotificationId;

    private bool _isRefreshing;
    private string _totalTrackedTime = "0m";
    private ObservableCollection<AppUsageGroup> _groupedApps = new();
    private Dictionary<string, TimeSpan> _lastUsageSnapshot = new();
    private string _todayFocusMinutes = "0m";
    private int _goalMinutes = 60;
    private double _goalProgress;
    private int _streakDays;
    private string _nextBestActionText = "Start a focus session";
    private string _nowAppName = "";
    private string _nowActivityState = "Neutral";
    private int _focusScore;
    private string _focusTrend = "flat";
    private int _sessionsToday;
    private readonly FocusScoreRingDrawable _ringDrawable = new();

    // tracks whether at least one remote load succeeded on this appearance
    private bool _anyLoadSucceeded;

    public MainPage() : this(
        RequireService<IActiveWindowTracker>(),
        RequireService<IAuthService>(),
        RequireService<IUsageService>(),
        RequireService<IProductivityService>(),
        RequireService<IFocusScoreService>())
    { }

    private static T RequireService<T>() where T : class =>
        MauiProgram.AppServices?.GetService<T>() ?? throw new InvalidOperationException($"{typeof(T).Name} not registered.");

    public MainPage(IActiveWindowTracker tracker, IAuthService authService,
        IUsageService usageService, IProductivityService productivityService,
        IFocusScoreService focusScoreService)
    {
        InitializeComponent();
        _tracker = tracker;
        _authService = authService;
        _usageService = usageService;
        _productivityService = productivityService;
        _focusScoreService = focusScoreService;
        BindingContext = this;

        ScoreRingView.Drawable = _ringDrawable;

        // Wire the banner retry action
        MainNetworkBanner.RetryAction = () =>
        {
            _ = Task.Run(async () =>
            {
                await LoadTodayFocusAsync();
                await LoadFocusScoreAsync();
                await LoadNextBestActionAsync();
            });
        };

        _uiTimer = new System.Timers.Timer(UiUpdateIntervalMs);
        _uiTimer.Elapsed += (s, e) => UpdateAppList();
        _uiTimer.AutoReset = true;

        _tracker.StartTracking();
        _uiTimer.Start();
        UpdateAppList();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await _authService.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }
        _usageService.StartPeriodicSync();
        _ = _usageService.SyncAsync();
        _anyLoadSucceeded = false;
        _ = LoadAllRemoteDataAsync();
        _uiTimer?.Start();
        GoalMinutes = ProductivityPreferences.GetDailyGoalMinutes();
    }

    private async Task LoadAllRemoteDataAsync()
    {
        var t1 = LoadTodayFocusAsync();
        var t2 = LoadFocusScoreAsync();
        var t3 = LoadNextBestActionAsync();
        await Task.WhenAll(t1, t2, t3);

        // If nothing came back, show the banner
        if (!_anyLoadSucceeded)
            await MainNetworkBanner.ShowBannerAsync();
    }

    // ── Properties ───────────────────────────────────────────────────────────

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set { if (_isRefreshing != value) { _isRefreshing = value; OnPropertyChanged(); } }
    }

    public string TotalTrackedTime
    {
        get => _totalTrackedTime;
        set { if (_totalTrackedTime != value) { _totalTrackedTime = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<AppUsageGroup> GroupedApps
    {
        get => _groupedApps;
        set { if (_groupedApps != value) { _groupedApps = value; OnPropertyChanged(); } }
    }

    public string TodayFocusMinutes
    {
        get => _todayFocusMinutes;
        set { if (_todayFocusMinutes != value) { _todayFocusMinutes = value; OnPropertyChanged(); OnPropertyChanged(nameof(GoalProgressLabel)); } }
    }

    public int SessionsToday
    {
        get => _sessionsToday;
        set { if (_sessionsToday != value) { _sessionsToday = value; OnPropertyChanged(); } }
    }

    public int GoalMinutes
    {
        get => _goalMinutes;
        set { if (_goalMinutes != value) { _goalMinutes = value; OnPropertyChanged(); OnPropertyChanged(nameof(GoalProgressLabel)); } }
    }

    public double GoalProgress
    {
        get => _goalProgress;
        set { if (Math.Abs(_goalProgress - value) > 0.001) { _goalProgress = value; OnPropertyChanged(); OnPropertyChanged(nameof(GoalPercentLabel)); } }
    }

    public string GoalProgressLabel => $"{TodayFocusMinutes} of {GoalMinutes} min goal";
    public string GoalPercentLabel => $"{(int)(GoalProgress * 100)}%";

    public int StreakDays
    {
        get => _streakDays;
        set { if (_streakDays != value) { _streakDays = value; OnPropertyChanged(); } }
    }

    public string NextBestActionText
    {
        get => _nextBestActionText;
        set { if (_nextBestActionText != value) { _nextBestActionText = value; OnPropertyChanged(); } }
    }

    public string NowDisplayName => string.IsNullOrEmpty(_nowAppName) ? "No active app" : _nowAppName;

    public string NowActivityState
    {
        get => _nowActivityState;
        set { if (_nowActivityState != value) { _nowActivityState = value; OnPropertyChanged(); OnPropertyChanged(nameof(NowStateColor)); } }
    }

    public Color NowStateColor => NowActivityState switch
    {
        "Focus"       => Color.FromArgb("#4ECCA3"),
        "Distraction" => Color.FromArgb("#E07A5F"),
        _             => Color.FromArgb("#6B9AC4")
    };

    public int FocusScore
    {
        get => _focusScore;
        set
        {
            if (_focusScore != value)
            {
                _focusScore = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FocusScoreLabel));
                OnPropertyChanged(nameof(FocusScoreColor));
                _ringDrawable.Score = value;
                Dispatcher.Dispatch(() => ScoreRingView.Invalidate());
            }
        }
    }

    public string FocusScoreLabel => FocusScore switch
    {
        >= 80 => "Excellent — you're in the zone",
        >= 60 => "Good focus day",
        >= 40 => "Room to improve",
        >= 20 => "Getting started",
        _     => "Let's build some momentum"
    };

    public Color FocusScoreColor => FocusScore switch
    {
        >= 60 => Color.FromArgb("#4ECCA3"),
        >= 30 => Color.FromArgb("#F5A623"),
        _     => Color.FromArgb("#E07A5F")
    };

    public string DayLabel
    {
        get
        {
            var day = DateTime.Now.DayOfWeek.ToString();
            var date = DateTime.Now.ToString("MMMM d");
            return $"{day}, {date}";
        }
    }

    public ICommand RefreshCommand => new Command(async () =>
    {
        IsRefreshing = true;
        _ = Task.Run(UpdateAppList);
        _anyLoadSucceeded = false;
        await LoadAllRemoteDataAsync();
        IsRefreshing = false;
    });

    public ICommand NextBestActionCommand => new Command(async () =>
    {
        if (!string.IsNullOrEmpty(_currentNotificationId))
            _ = _productivityService.MarkAsReadAsync(_currentNotificationId);
        await Shell.Current.GoToAsync("///SessionPage");
    });

    // ── Remote data loaders ───────────────────────────────────────────────────

    private async Task LoadFocusScoreAsync()
    {
        try
        {
            var result = await _focusScoreService.GetFocusScoreAsync();
            if (result == null) return;
            _anyLoadSucceeded = true;
            Dispatcher.Dispatch(async () =>
            {
                FocusScore = result.Score;
                _focusTrend = result.Trend;
                _ringDrawable.Trend = result.Trend;
                SessionsToday = result.SessionsToday;
                StreakDays = result.StreakDays;
                ScoreRingView.Invalidate();
                await MainNetworkBanner.HideBannerAsync();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadFocusScore: {ex.Message}");
            NetworkStatusService.Instance.ReportFailure(ex);
        }
    }

    private async Task LoadNextBestActionAsync()
    {
        try
        {
            var items = await _productivityService.GetNotificationsAsync(unreadOnly: true);
            _anyLoadSucceeded = true;

            // Show weekly digest modal if one is unread
            var digest = items.FirstOrDefault(n =>
                string.Equals(n.Type, "weekly_digest", StringComparison.OrdinalIgnoreCase));
            if (digest != null)
            {
                var digestPage = MauiProgram.AppServices?.GetService<DigestPage>();
                if (digestPage != null)
                {
                    digestPage.LoadDigest(digest);
                    await Shell.Current.GoToAsync(nameof(DigestPage));
                }
            }

            var first = items.FirstOrDefault(n =>
                string.Equals(n.Type, "recommendation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Type, "insight", StringComparison.OrdinalIgnoreCase));
            if (first != null && !string.IsNullOrWhiteSpace(first.Title))
            {
                _nextBestActionFromServer = first.Title;
                _currentNotificationId = first.Id;
            }
            else
            {
                _nextBestActionFromServer = null;
                _currentNotificationId = null;
            }
            var fallback = GoalProgress >= 1.0 ? "You hit your daily goal!" : "Start a 25 min focus session";
            NextBestActionText = _nextBestActionFromServer ?? fallback;

            await MainNetworkBanner.HideBannerAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadNextBestAction: {ex.Message}");
            NetworkStatusService.Instance.ReportFailure(ex);
        }
    }

    private async Task LoadTodayFocusAsync()
    {
        try
        {
            var from = DateTime.Today.ToString("yyyy-MM-dd");
            var response = await _usageService.GetUsageAsync(from, from, "day", null, null);
            if (response?.Entries == null || response.Entries.Count == 0)
            {
                TodayFocusMinutes = "0m";
                GoalProgress = 0;
                // A 200 with empty entries still counts as success
                _anyLoadSucceeded = true;
                await MainNetworkBanner.HideBannerAsync();
                return;
            }
            _anyLoadSucceeded = true;
            var focusSeconds = response.Entries
                .Where(e => string.Equals(e.Category, "Coding", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.Category, "Productivity", StringComparison.OrdinalIgnoreCase))
                .Sum(e => e.TotalSeconds);
            var focusMinutes = (int)(focusSeconds / 60);
            TodayFocusMinutes = focusMinutes >= 60
                ? $"{focusMinutes / 60}h {focusMinutes % 60}m"
                : $"{focusMinutes}m";
            GoalProgress = _goalMinutes <= 0 ? 0 : Math.Min(1.0, (double)focusMinutes / _goalMinutes);
            await MainNetworkBanner.HideBannerAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadTodayFocus: {ex.Message}");
            NetworkStatusService.Instance.ReportFailure(ex);
        }
    }

    // ── Local tracker update ──────────────────────────────────────────────────

    private void UpdateAppList()
    {
        if (_updateInProgress) return;
        try
        {
            _updateInProgress = true;
            var currentUsage = _tracker.GetAppUsage();

            // Skip full rebuild when usage data hasn't meaningfully changed (>1s tolerance)
            bool changed = currentUsage.Count != _lastUsageSnapshot.Count ||
                currentUsage.Any(kv =>
                    !_lastUsageSnapshot.TryGetValue(kv.Key, out var prev) ||
                    Math.Abs((kv.Value - prev).TotalSeconds) > 1);
            if (!changed)
            {
                _updateInProgress = false;
                return;
            }
            _lastUsageSnapshot = new Dictionary<string, TimeSpan>(currentUsage);

            var newGroups = new Dictionary<string, List<AppUsageInfo>>();

            foreach (var kvp in currentUsage)
            {
                var category = CategoryResolver.ResolveCategory(kvp.Key);
                if (!newGroups.ContainsKey(category))
                    newGroups[category] = new List<AppUsageInfo>();

                var existingItem = GroupedApps.SelectMany(g => g).FirstOrDefault(x => x.AppName == kvp.Key);
                if (existingItem != null)
                    existingItem.RunningTime = kvp.Value;
                else
                    newGroups[category].Add(new AppUsageInfo { AppName = kvp.Key, Category = category, RunningTime = kvp.Value });
            }

            Dispatcher.Dispatch(() =>
            {
                foreach (var group in GroupedApps.ToList())
                    if (!newGroups.ContainsKey(group.Category))
                        GroupedApps.Remove(group);

                foreach (var kvp in newGroups)
                {
                    var existing = GroupedApps.FirstOrDefault(g => g.Category == kvp.Key);
                    if (existing != null)
                    {
                        foreach (var item in kvp.Value)
                            if (!existing.Any(x => x.AppName == item.AppName))
                                existing.Add(item);
                        for (var i = existing.Count - 1; i >= 0; i--)
                            if (!currentUsage.ContainsKey(existing[i].AppName))
                                existing.RemoveAt(i);
                    }
                    else
                    {
                        GroupedApps.Add(new AppUsageGroup(kvp.Key, kvp.Value));
                    }
                }

                foreach (var group in GroupedApps)
                {
                    var sorted = group.OrderByDescending(x => x.RunningTime).ToList();
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var currentIndex = group.IndexOf(sorted[i]);
                        if (currentIndex != i)
                            group.Move(currentIndex, i);
                    }
                }

                UpdateStatistics();
                UpdateNow();
                _updateInProgress = false;
            });
        }
        catch (Exception ex)
        {
            _updateInProgress = false;
            Debug.WriteLine($"UpdateAppList: {ex}");
            Dispatcher.Dispatch(() => { GroupedApps.Clear(); _updateInProgress = false; });
        }
    }

    private void UpdateStatistics()
    {
        var totalTime = GroupedApps.SelectMany(g => g).Aggregate(TimeSpan.Zero, (sum, item) => sum + item.RunningTime);
        TotalTrackedTime = totalTime.TotalHours >= 1
            ? $"{(int)totalTime.TotalHours}h {totalTime.Minutes}m"
            : $"{totalTime.Minutes}m";
    }

    private void UpdateNow()
    {
        var top = GroupedApps.SelectMany(g => g).OrderByDescending(x => x.RunningTime).FirstOrDefault();
        if (top != null)
        {
            _nowAppName = top.AppName;
            NowActivityState = top.Category switch
            {
                "Coding" or "Productivity" => "Focus",
                "Gaming" or "Chilling"     => "Distraction",
                _                          => "Neutral"
            };
        }
        else
        {
            var currentApp = _tracker.GetCurrentAppName();
            _nowAppName = string.IsNullOrEmpty(currentApp) ? "" : currentApp;
            NowActivityState = _tracker.GetCurrentCategory() switch
            {
                "Coding" or "Productivity" => "Focus",
                "Gaming" or "Chilling"     => "Distraction",
                _                          => "Neutral"
            };
        }
        OnPropertyChanged(nameof(NowDisplayName));
        var fallback = GoalProgress >= 1.0 ? "You hit your daily goal!" : "Start a 25 min focus session";
        NextBestActionText = _nextBestActionFromServer ?? fallback;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _uiTimer?.Stop();
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _usageService.StopPeriodicSync();
        _authService.Logout();
        await Shell.Current.GoToAsync(nameof(LoginPage));
    }

    private async void OnStartSessionClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("///SessionPage");

    private async void OnAddTaskClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("///TasksPage");

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
