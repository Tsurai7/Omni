using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Omni.Client.Models.Calendar;
using Omni.Client.Services;

namespace Omni.Client.Presentation.ViewModels;

public enum CalendarViewMode { Month, Week, Day }

public partial class CalendarViewModel : ObservableObject
{
    private readonly CalendarService _calendarService;
    private DateTime _lastLoaded = DateTime.MinValue;

    public bool IsDataStale(TimeSpan threshold) => DateTime.UtcNow - _lastLoaded > threshold;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeLabel))]
    private CalendarViewMode _viewMode = CalendarViewMode.Month;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDateLabel))]
    private DateTime _currentDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<CalendarEvent> _events = new();

    [ObservableProperty]
    private bool _isLoading;

    public string ViewModeLabel => ViewMode switch
    {
        CalendarViewMode.Week => "Week",
        CalendarViewMode.Day  => "Day",
        _                     => "Month"
    };

    public string CurrentDateLabel => ViewMode switch
    {
        CalendarViewMode.Month => CurrentDate.ToString("MMMM yyyy"),
        CalendarViewMode.Week  => $"Week of {GetWeekStart().ToString("MMM d")}",
        CalendarViewMode.Day   => CurrentDate.ToString("dddd, MMM d"),
        _                      => CurrentDate.ToString("MMMM yyyy")
    };

    public event Action? EventsLoaded;

    public CalendarViewModel(CalendarService calendarService)
    {
        _calendarService = calendarService;
    }

    [RelayCommand]
    public async Task LoadEventsAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var (start, end) = GetDateRange();
            var evts = await _calendarService.GetEventsAsync(start, end, ct);
            Events = new ObservableCollection<CalendarEvent>(evts);
            _lastLoaded = DateTime.UtcNow;
            EventsLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarViewModel.LoadEventsAsync: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task CreateEventAsync(
        string title, DateTime start, DateTime? end, bool isAllDay,
        string? description = null,
        CancellationToken ct = default)
    {
        try
        {
            var success = await _calendarService.CreateGoogleEventAsync(title, start, end, isAllDay, description, ct);
            if (success)
                await LoadEventsAsync(ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarViewModel.CreateEventAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    public void Navigate(int direction)
    {
        CurrentDate = ViewMode switch
        {
            CalendarViewMode.Month => CurrentDate.AddMonths(direction),
            CalendarViewMode.Week  => CurrentDate.AddDays(7 * direction),
            CalendarViewMode.Day   => CurrentDate.AddDays(direction),
            _                      => CurrentDate.AddMonths(direction)
        };
    }

    [RelayCommand]
    public async Task SetViewModeAsync(CalendarViewMode mode)
    {
        ViewMode = mode;
        await LoadEventsAsync();
    }

    public void Invalidate() => _lastLoaded = DateTime.MinValue;

    private (DateTime start, DateTime end) GetDateRange()
    {
        return ViewMode switch
        {
            CalendarViewMode.Month =>
                (new DateTime(CurrentDate.Year, CurrentDate.Month, 1),
                 new DateTime(CurrentDate.Year, CurrentDate.Month, 1).AddMonths(1)),
            CalendarViewMode.Week =>
                (GetWeekStart(), GetWeekStart().AddDays(7)),
            CalendarViewMode.Day =>
                (CurrentDate.Date, CurrentDate.Date.AddDays(1)),
            _ =>
                (new DateTime(CurrentDate.Year, CurrentDate.Month, 1),
                 new DateTime(CurrentDate.Year, CurrentDate.Month, 1).AddMonths(1))
        };
    }

    private DateTime GetWeekStart()
    {
        var diff = (7 + (CurrentDate.DayOfWeek - DayOfWeek.Monday)) % 7;
        return CurrentDate.AddDays(-diff).Date;
    }
}
