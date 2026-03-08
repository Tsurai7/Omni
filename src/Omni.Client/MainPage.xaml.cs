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
        _uiTimer?.Start();
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

    // Команда для RefreshView
    public ICommand RefreshCommand => new Command(async () =>
    {
        IsRefreshing = true;
        await Task.Run(() => UpdateAppList());
        IsRefreshing = false;
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
                        for (int i = existingGroup.Count - 1; i >= 0; i--)
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

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
}