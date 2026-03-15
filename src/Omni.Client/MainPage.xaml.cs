using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Input;
using Omni.Client.Abstractions;
using Omni.Client.Models;

namespace Omni.Client;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    private readonly IActiveWindowTracker _tracker;
    private readonly IAuthService _authService;
    private readonly IUsageService _usageService;
    private readonly System.Timers.Timer _uiTimer;
    private const int UiUpdateIntervalMs = 2500; // Reduced from 1s to lower CPU usage
    private bool _updateInProgress;

    // Для привязок в XAML
    private bool _isRefreshing;
    private string _totalTrackedTime = "0h 0m";
    private string _currentCategory = "None";
    private ObservableCollection<AppUsageGroup> _groupedApps = new();
    private string _todayFocusMinutes = "0 min";
    private int _goalMinutes = 60;
    private double _goalProgress;
    private int _streakDays;
    private string _nextBestActionText = "Start a focus session";
    private string _nowAppName = "";
    private string _nowActivityState = "Neutral";

    public MainPage(IActiveWindowTracker tracker, IAuthService authService, IUsageService usageService)
    {
        InitializeComponent();
        _tracker = tracker;
        _authService = authService;
        _usageService = usageService;
        BindingContext = this; // Устанавливаем BindingContext на саму страницу
        
        // Инициализация таймера обновления UI (реже = меньше CPU)
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
        _ = _usageService.SyncAsync(); // initial sync
        _ = LoadTodayFocusAsync();
        _uiTimer?.Start();
        GoalMinutes = ProductivityPreferences.GetDailyGoalMinutes();
    }

    // Свойства для привязок
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            if (_isRefreshing != value)
            {
                _isRefreshing = value;
                OnPropertyChanged();
            }
        }
    }

    public string TotalTrackedTime
    {
        get => _totalTrackedTime;
        set
        {
            if (_totalTrackedTime != value)
            {
                _totalTrackedTime = value;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentCategory
    {
        get => _currentCategory;
        set
        {
            if (_currentCategory != value)
            {
                _currentCategory = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<AppUsageGroup> GroupedApps
    {
        get => _groupedApps;
        set
        {
            if (_groupedApps != value)
            {
                _groupedApps = value;
                OnPropertyChanged();
            }
        }
    }

    public string TodayFocusMinutes
    {
        get => _todayFocusMinutes;
        set { if (_todayFocusMinutes != value) { _todayFocusMinutes = value; OnPropertyChanged(); OnPropertyChanged(nameof(GoalProgressLabel)); } }
    }

    public int GoalMinutes
    {
        get => _goalMinutes;
        set { if (_goalMinutes != value) { _goalMinutes = value; OnPropertyChanged(); OnPropertyChanged(nameof(GoalProgressLabel)); } }
    }

    public double GoalProgress
    {
        get => _goalProgress;
        set { if (Math.Abs(_goalProgress - value) > 0.001) { _goalProgress = value; OnPropertyChanged(); OnPropertyChanged(nameof(GoalProgressLabel)); } }
    }

    public string GoalProgressLabel => $"{TodayFocusMinutes} / {GoalMinutes} min";

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

    public string NowAppName
    {
        get => _nowAppName;
        set { if (_nowAppName != value) { _nowAppName = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNowVisible)); } }
    }

    public string NowActivityState
    {
        get => _nowActivityState;
        set { if (_nowActivityState != value) { _nowActivityState = value; OnPropertyChanged(); OnPropertyChanged(nameof(NowStateColor)); } }
    }

    public Color NowStateColor => NowActivityState switch
    {
        "Focus" => Color.FromArgb("#4ECCA3"),
        "Distraction" => Color.FromArgb("#E07A5F"),
        _ => Color.FromArgb("#6B9AC4")
    };

    public bool IsNowVisible => !string.IsNullOrEmpty(NowAppName);

    // Команда для RefreshView
    public ICommand RefreshCommand => new Command(async () =>
    {
        IsRefreshing = true;
        await Task.Run(() => UpdateAppList());
        await LoadTodayFocusAsync();
        IsRefreshing = false;
    });

    public ICommand NextBestActionCommand => new Command(async () =>
    {
        await Shell.Current.GoToAsync(nameof(SessionPage));
    });

    private void UpdateAppList()
    {
        if (_updateInProgress)
            return;
        try
        {
            _updateInProgress = true;
            var currentUsage = _tracker.GetAppUsage();
            var newGroups = new Dictionary<string, List<AppUsageInfo>>();

            // Группируем приложения по категориям
            foreach (var kvp in currentUsage)
            {
                var category = CategoryResolver.ResolveCategory(kvp.Key);
                if (!newGroups.ContainsKey(category))
                {
                    newGroups[category] = new List<AppUsageInfo>();
                }

                var existingItem = GroupedApps
                    .SelectMany(g => g)
                    .FirstOrDefault(x => x.AppName == kvp.Key);

                if (existingItem != null)
                {
                    existingItem.RunningTime = kvp.Value;
                }
                else
                {
                    newGroups[category].Add(new AppUsageInfo
                    {
                        AppName = kvp.Key,
                        Category = category,
                        RunningTime = kvp.Value
                    });
                }
            }

            // Обновляем GroupedApps в UI потоке
            Dispatcher.Dispatch(() =>
            {
                // Удаляем пустые категории
                foreach (var group in GroupedApps.ToList())
                {
                    if (!newGroups.ContainsKey(group.Category))
                    {
                        GroupedApps.Remove(group);
                    }
                }

                // Обновляем или добавляем категории
                foreach (var kvp in newGroups)
                {
                    var existingGroup = GroupedApps.FirstOrDefault(g => g.Category == kvp.Key);
                    if (existingGroup != null)
                    {
                        // Обновляем существующую группу
                        foreach (var item in kvp.Value)
                        {
                            if (!existingGroup.Any(x => x.AppName == item.AppName))
                            {
                                existingGroup.Add(item);
                            }
                        }

                        // Удаляем отсутствующие приложения
                        for (var i = existingGroup.Count - 1; i >= 0; i--)
                        {
                            if (!currentUsage.ContainsKey(existingGroup[i].AppName))
                            {
                                existingGroup.RemoveAt(i);
                            }
                        }
                    }
                    else
                    {
                        // Добавляем новую группу
                        GroupedApps.Add(new AppUsageGroup(kvp.Key, kvp.Value));
                    }
                }

                // Сортируем группы по времени
                foreach (var group in GroupedApps)
                {
                    var sorted = group.OrderByDescending(x => x.RunningTime).ToList();
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        group.Move(group.IndexOf(sorted[i]), i);
                    }
                }

                // Обновляем статистику
                UpdateStatistics();
                UpdateNowAndNextAction();
                _updateInProgress = false;
            });
        }
        catch (Exception ex)
        {
            _updateInProgress = false;
            Debug.WriteLine($"Ошибка обновления: {ex}");
            Dispatcher.Dispatch(() =>
            {
                GroupedApps.Clear();
                GroupedApps.Add(new AppUsageGroup("System", new List<AppUsageInfo>
                {
                    new() { AppName = "Ошибка получения данных", RunningTime = TimeSpan.Zero }
                }));
                _updateInProgress = false;
            });
        }
    }

    private void UpdateStatistics()
    {
        // Общее время
        var totalTime = GroupedApps
            .SelectMany(g => g)
            .Aggregate(TimeSpan.Zero, (sum, item) => sum + item.RunningTime);

        TotalTrackedTime = $"{totalTime.Hours}h {totalTime.Minutes}m";

        // Самая активная категория
        var mostActiveCategory = GroupedApps
            .OrderByDescending(g => g.Sum(x => x.RunningTime.Ticks))
            .FirstOrDefault()?.Category ?? "None";

        CurrentCategory = mostActiveCategory;
    }

    private void UpdateNowAndNextAction()
    {
        var top = GroupedApps
            .SelectMany(g => g)
            .OrderByDescending(x => x.RunningTime.Ticks)
            .FirstOrDefault();
        if (top != null)
        {
            NowAppName = top.AppName;
            var state = top.Category switch
            {
                "Coding" or "Productivity" => "Focus",
                "Gaming" or "Chilling" => "Distraction",
                _ => "Neutral"
            };
            NowActivityState = state;
        }
        else
        {
            NowAppName = "";
        }
        NextBestActionText = GoalProgress >= 1.0 ? "You hit your goal" : "Start a focus session";
    }

    private async Task LoadTodayFocusAsync()
    {
        try
        {
            var from = DateTime.Today.ToString("yyyy-MM-dd");
            var to = DateTime.Today.ToString("yyyy-MM-dd");
            var response = await _usageService.GetUsageAsync(from, to, "day", null, null);
            if (response?.Entries == null || response.Entries.Count == 0)
            {
                TodayFocusMinutes = "0 min";
                GoalProgress = 0;
                return;
            }
            const string focusCategory1 = "Coding";
            const string focusCategory2 = "Productivity";
            var focusSeconds = response.Entries
                .Where(e => string.Equals(e.Category, focusCategory1, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.Category, focusCategory2, StringComparison.OrdinalIgnoreCase))
                .Sum(e => e.TotalSeconds);
            var focusMinutes = (int)(focusSeconds / 60);
            TodayFocusMinutes = $"{focusMinutes} min";
            GoalProgress = _goalMinutes <= 0 ? 0 : Math.Min(1.0, (double)focusMinutes / _goalMinutes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadTodayFocus: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Keep sync running when navigating away (e.g. to Usage stats); only stop on logout
        _uiTimer?.Stop();
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _usageService.StopPeriodicSync();
        _authService.Logout();
        await Shell.Current.GoToAsync(nameof(LoginPage));
    }

    private async void OnStartSessionClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(SessionPage));
    }

    private async void OnAddTaskClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(TasksPage));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
}