using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Omni.Client.Abstractions;
using Omni.Client.Helpers;
using Omni.Client.Models;
using Omni.Client.Services;

namespace Omni.Client.Presentation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IActiveWindowTracker _tracker;
    private readonly IAuthService _authService;
    private readonly IUsageService _usageService;
    private readonly IProductivityService _productivityService;
    private readonly IFocusScoreService _focusScoreService;

    private string? _nextBestActionFromServer;
    private string? _currentNotificationId;
    private Dictionary<string, TimeSpan> _lastUsageSnapshot = new();
    private bool _updateInProgress;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _totalTrackedTime = "0m";

    [ObservableProperty]
    private ObservableCollection<AppUsageGroup> _groupedApps = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoalProgressLabel))]
    private string _todayFocusMinutes = "0m";

    [ObservableProperty]
    private int _sessionsToday;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoalProgressLabel))]
    private int _goalMinutes = 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoalPercentLabel))]
    private double _goalProgress;

    [ObservableProperty]
    private int _streakDays;

    [ObservableProperty]
    private string _nextBestActionText = "Start a focus session";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowDisplayName))]
    private string _nowAppName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowStateColor))]
    private string _nowActivityState = "Neutral";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusScoreLabel), nameof(FocusScoreColor))]
    private int _focusScore;

    public string GoalProgressLabel => $"{TodayFocusMinutes} of {GoalMinutes} min goal";
    public string GoalPercentLabel  => $"{(int)(GoalProgress * 100)}%";
    public string NowDisplayName    => string.IsNullOrEmpty(NowAppName) ? "No active app" : NowAppName;

    public Color NowStateColor => NowActivityState switch
    {
        "Focus"       => Color.FromArgb("#4ECCA3"),
        "Distraction" => Color.FromArgb("#E07A5F"),
        _             => Color.FromArgb("#6B9AC4")
    };

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
            var day  = DateTime.Now.DayOfWeek.ToString();
            var date = DateTime.Now.ToString("MMMM d");
            return $"{day}, {date}";
        }
    }

    public string FocusTrend { get; private set; } = "flat";

    public event Action<int, string>? FocusScoreUpdated;

    public MainViewModel(
        IActiveWindowTracker tracker,
        IAuthService authService,
        IUsageService usageService,
        IProductivityService productivityService,
        IFocusScoreService focusScoreService)
    {
        _tracker = tracker;
        _authService = authService;
        _usageService = usageService;
        _productivityService = productivityService;
        _focusScoreService = focusScoreService;
    }

    [RelayCommand]
    public async Task LoadAllRemoteDataAsync()
    {
        var t1 = LoadTodayFocusAsync();
        var t2 = LoadFocusScoreAsync();
        var t3 = LoadNextBestActionAsync();
        await Task.WhenAll(t1, t2, t3);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        UpdateAppList();
        await LoadAllRemoteDataAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    public async Task MarkNotificationReadAsync()
    {
        if (!string.IsNullOrEmpty(_currentNotificationId))
            _ = _productivityService.MarkAsReadAsync(_currentNotificationId);
        await Shell.Current.GoToAsync("///SessionPage");
    }

    public async Task<bool> IsAuthenticatedAsync()
        => await _authService.IsAuthenticatedAsync();

    public void StartUsageSync()
    {
        _usageService.StartPeriodicSync();
        _ = _usageService.SyncAsync();
    }

    public void StopUsageSync() => _usageService.StopPeriodicSync();

    public void Logout() => _authService.Logout();

    public void LoadGoalFromPreferences()
        => GoalMinutes = ProductivityPreferences.GetDailyGoalMinutes();

    public void UpdateAppList()
    {
        if (_updateInProgress) return;
        try
        {
            _updateInProgress = true;
            var currentUsage = _tracker.GetAppUsage();

            bool changed = currentUsage.Count != _lastUsageSnapshot.Count ||
                currentUsage.Any(kv =>
                    !_lastUsageSnapshot.TryGetValue(kv.Key, out var prev) ||
                    Math.Abs((kv.Value - prev).TotalSeconds) > 1);
            if (!changed)
            {
                _updateInProgress = false;
                UpdateNow();
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
        }
        catch (Exception ex)
        {
            _updateInProgress = false;
            Debug.WriteLine($"UpdateAppList: {ex}");
            GroupedApps.Clear();
            _updateInProgress = false;
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
            NowAppName = top.AppName;
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
            NowAppName = string.IsNullOrEmpty(currentApp) ? "" : currentApp;
            NowActivityState = _tracker.GetCurrentCategory() switch
            {
                "Coding" or "Productivity" => "Focus",
                "Gaming" or "Chilling"     => "Distraction",
                _                          => "Neutral"
            };
        }
        var fallback = GoalProgress >= 1.0 ? "You hit your daily goal!" : "Start a 25 min focus session";
        NextBestActionText = _nextBestActionFromServer ?? fallback;
    }

    private async Task LoadFocusScoreAsync()
    {
        try
        {
            var result = await _focusScoreService.GetFocusScoreAsync();
            if (result == null) return;
            FocusScore = result.Score;
            FocusTrend = result.Trend;
            SessionsToday = result.SessionsToday;
            StreakDays = result.StreakDays;
            FocusScoreUpdated?.Invoke(result.Score, result.Trend);
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
            var response = await _usageService.GetUsageAsync(from, from, "day", null, null, DeviceLocalTime.UtcOffsetMinutes);
            if (response?.Entries == null || response.Entries.Count == 0)
            {
                TodayFocusMinutes = "0m";
                GoalProgress = 0;
                return;
            }
            var focusSeconds = response.Entries
                .Where(e => string.Equals(e.Category, "Coding", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.Category, "Productivity", StringComparison.OrdinalIgnoreCase))
                .Sum(e => e.TotalSeconds);
            var focusMinutes = (int)(focusSeconds / 60);
            TodayFocusMinutes = focusMinutes >= 60
                ? $"{focusMinutes / 60}h {focusMinutes % 60}m"
                : $"{focusMinutes}m";
            GoalProgress = GoalMinutes <= 0 ? 0 : Math.Min(1.0, (double)focusMinutes / GoalMinutes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadTodayFocus: {ex.Message}");
            NetworkStatusService.Instance.ReportFailure(ex);
        }
    }

    public void StartTracking() => _tracker.StartTracking();
}
