using System.ComponentModel;
using System.Runtime.CompilerServices;
using Omni.Client.Abstractions;
using Omni.Client.Models.Calendar;
using Omni.Client.Services;

namespace Omni.Client;

public partial class CalendarPage : ContentPage, INotifyPropertyChanged
{
    private CalendarService? _calendarService;
    private ITaskService? _taskService;

    private DateTime _displayedMonth;
    private DateTime _selectedDay;
    private List<CalendarEvent> _events = new();

    private enum ViewMode { Month, Week, Day }
    private ViewMode _viewMode = ViewMode.Month;

    public CalendarPage()
    {
        InitializeComponent();
        _displayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _selectedDay = DateTime.Today;
    }

    private CalendarService GetCalendarService() =>
        _calendarService ??= MauiProgram.AppServices?.GetService<CalendarService>()!;

    private ITaskService GetTaskService() =>
        _taskService ??= MauiProgram.AppServices?.GetService<ITaskService>()!;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var auth = MauiProgram.AppServices?.GetService<IAuthService>();
        if (auth != null && !await auth.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        UpdateHeader();
        await LoadEventsAsync();
        RenderCurrentView();
        await UpdateSyncStatusAsync();
    }

    // ── Header / navigation ───────────────────────────────────────────────

    private void UpdateHeader()
    {
        MonthYearLabel.Text = _viewMode switch
        {
            ViewMode.Week => $"Week of {GetWeekStart(_selectedDay):MMM d, yyyy}",
            ViewMode.Day  => _selectedDay.ToString("MMMM d, yyyy"),
            _             => _displayedMonth.ToString("MMMM yyyy"),
        };
    }

    private async void OnPrevClicked(object? sender, EventArgs e)
    {
        switch (_viewMode)
        {
            case ViewMode.Month:
                _displayedMonth = _displayedMonth.AddMonths(-1);
                break;
            case ViewMode.Week:
                _selectedDay = _selectedDay.AddDays(-7);
                break;
            case ViewMode.Day:
                _selectedDay = _selectedDay.AddDays(-1);
                break;
        }
        await RefreshAsync();
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        switch (_viewMode)
        {
            case ViewMode.Month:
                _displayedMonth = _displayedMonth.AddMonths(1);
                break;
            case ViewMode.Week:
                _selectedDay = _selectedDay.AddDays(7);
                break;
            case ViewMode.Day:
                _selectedDay = _selectedDay.AddDays(1);
                break;
        }
        await RefreshAsync();
    }

    private async void OnTodayClicked(object? sender, EventArgs e)
    {
        _selectedDay = DateTime.Today;
        _displayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        await RefreshAsync();
    }

    // ── View mode switches ────────────────────────────────────────────────

    private async void OnMonthViewClicked(object? sender, EventArgs e)
    {
        _viewMode = ViewMode.Month;
        SetViewButtons();
        await RefreshAsync();
    }

    private async void OnWeekViewClicked(object? sender, EventArgs e)
    {
        _viewMode = ViewMode.Week;
        SetViewButtons();
        await RefreshAsync();
    }

    private async void OnDayViewClicked(object? sender, EventArgs e)
    {
        _viewMode = ViewMode.Day;
        SetViewButtons();
        await RefreshAsync();
    }

    private void SetViewButtons()
    {
        MonthViewBtn.Style = _viewMode == ViewMode.Month
            ? (Style)Resources["ProductivityPillButton"]
            : (Style)Resources["ProductivitySecondaryButton"];
        WeekViewBtn.Style = _viewMode == ViewMode.Week
            ? (Style)Resources["ProductivityPillButton"]
            : (Style)Resources["ProductivitySecondaryButton"];
        DayViewBtn.Style = _viewMode == ViewMode.Day
            ? (Style)Resources["ProductivityPillButton"]
            : (Style)Resources["ProductivitySecondaryButton"];

        MonthView.IsVisible = _viewMode == ViewMode.Month;
        WeekView.IsVisible  = _viewMode == ViewMode.Week;
        DayView.IsVisible   = _viewMode == ViewMode.Day;
    }

    // ── Data loading ──────────────────────────────────────────────────────

    private async Task LoadEventsAsync()
    {
        var (start, end) = GetCurrentDateRange();
        try
        {
            _events = await GetCalendarService().GetEventsAsync(start, end);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CalendarPage.LoadEventsAsync: {ex.Message}");
            _events = new List<CalendarEvent>();
        }
    }

    private (DateTime start, DateTime end) GetCurrentDateRange() => _viewMode switch
    {
        ViewMode.Week => (GetWeekStart(_selectedDay), GetWeekStart(_selectedDay).AddDays(7)),
        ViewMode.Day  => (_selectedDay.Date, _selectedDay.Date.AddDays(1)),
        _             => (_displayedMonth, _displayedMonth.AddMonths(1)),
    };

    // ── Rendering ─────────────────────────────────────────────────────────

    private void RenderCurrentView()
    {
        UpdateHeader();
        switch (_viewMode)
        {
            case ViewMode.Month: RenderMonthView(); break;
            case ViewMode.Week:  RenderWeekView();  break;
            case ViewMode.Day:   RenderDayView();   break;
        }
    }

    private void RenderMonthView()
    {
        MonthGridContainer.Children.Clear();

        var firstDay = _displayedMonth;
        var startOfGrid = firstDay.AddDays(-(int)firstDay.DayOfWeek); // back to Sunday

        for (int week = 0; week < 6; week++)
        {
            var weekRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(
                    Enumerable.Repeat(new ColumnDefinition(GridLength.Star), 7).ToArray()),
                ColumnSpacing = 2,
            };

            for (int dayOfWeek = 0; dayOfWeek < 7; dayOfWeek++)
            {
                var date = startOfGrid.AddDays(week * 7 + dayOfWeek);
                var isCurrentMonth = date.Month == _displayedMonth.Month;
                var isToday = date.Date == DateTime.Today;

                var dayEvents = _events.Where(e => e.StartAt.Date == date.Date).ToList();

                var dayCell = BuildDayCell(date, isCurrentMonth, isToday, dayEvents);
                weekRow.Add(dayCell, dayOfWeek, 0);
            }

            MonthGridContainer.Children.Add(weekRow);
        }
    }

    private View BuildDayCell(DateTime date, bool isCurrentMonth, bool isToday, List<CalendarEvent> dayEvents)
    {
        var cellBg = isCurrentMonth
            ? Color.FromArgb("#1A1A1F")
            : Color.FromArgb("#13131A");

        var border = new Border
        {
            Padding = new Thickness(4, 4, 4, 6),
            BackgroundColor = cellBg,
            Stroke = isToday ? Color.FromArgb("#7C6AF7") : Color.FromArgb("#2A2A35"),
            StrokeThickness = isToday ? 1.5 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            HeightRequest = 72,
        };

        var content = new VerticalStackLayout { Spacing = 2 };

        // Day number
        var dayNumLabel = new Label
        {
            Text = date.Day.ToString(),
            FontSize = 13,
            FontAttributes = isToday ? FontAttributes.Bold : FontAttributes.None,
            TextColor = isToday
                ? Color.FromArgb("#7C6AF7")
                : isCurrentMonth
                    ? Color.FromArgb("#E0E0F0")
                    : Color.FromArgb("#44444F"),
            HorizontalOptions = LayoutOptions.Start,
        };
        content.Children.Add(dayNumLabel);

        // Event dots/chips (max 3)
        var dotsRow = new HorizontalStackLayout { Spacing = 3 };
        foreach (var evt in dayEvents.Take(3))
        {
            dotsRow.Children.Add(new BoxView
            {
                WidthRequest = 6, HeightRequest = 6,
                CornerRadius = 3,
                Color = evt.EventColor,
                VerticalOptions = LayoutOptions.Center,
            });
        }
        if (dayEvents.Count > 3)
        {
            dotsRow.Children.Add(new Label
            {
                Text = $"+{dayEvents.Count - 3}",
                FontSize = 9,
                TextColor = Color.FromArgb("#66667A"),
                VerticalOptions = LayoutOptions.Center,
            });
        }
        if (dotsRow.Children.Count > 0)
            content.Children.Add(dotsRow);

        // Small event chip (first event title)
        if (dayEvents.Count > 0)
        {
            var chipText = new Label
            {
                Text = dayEvents[0].Title,
                FontSize = 10,
                TextColor = dayEvents[0].EventColor,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
            };
            content.Children.Add(chipText);
        }

        border.Content = content;

        // Tap to open detail sheet
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => ShowDayDetail(date, dayEvents))
        });

        return border;
    }

    private void RenderWeekView()
    {
        var weekStart = GetWeekStart(_selectedDay);
        WeekTimeGrid.Children.Clear();
        WeekTimeGrid.RowDefinitions.Clear();

        // Build week day headers
        WeekDayHeaders.Children.Clear();
        WeekDayHeaders.ColumnDefinitions.Clear();
        WeekDayHeaders.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
        for (int i = 0; i < 7; i++)
            WeekDayHeaders.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        WeekDayHeaders.Add(new Label
        {
            Text = "",
            FontSize = 11,
            TextColor = Color.FromArgb("#44444F"),
            HorizontalOptions = LayoutOptions.Center,
        }, 0, 0);

        for (int d = 0; d < 7; d++)
        {
            var day = weekStart.AddDays(d);
            var isToday = day.Date == DateTime.Today;
            var col = d + 1;

            var stack = new VerticalStackLayout { Spacing = 1, HorizontalOptions = LayoutOptions.Center };
            stack.Children.Add(new Label
            {
                Text = day.ToString("ddd"),
                FontSize = 10,
                TextColor = isToday ? Color.FromArgb("#7C6AF7") : Color.FromArgb("#66667A"),
                HorizontalOptions = LayoutOptions.Center,
            });
            stack.Children.Add(new Border
            {
                WidthRequest = 28, HeightRequest = 28,
                BackgroundColor = isToday ? Color.FromArgb("#7C6AF7") : Colors.Transparent,
                Stroke = Colors.Transparent,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                Content = new Label
                {
                    Text = day.Day.ToString(),
                    FontSize = 13,
                    FontAttributes = isToday ? FontAttributes.Bold : FontAttributes.None,
                    TextColor = isToday ? Colors.White : Color.FromArgb("#E0E0F0"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                },
            });
            WeekDayHeaders.Add(stack, col, 0);
        }

        // Time grid rows (6am – 11pm = 18 rows)
        const int startHour = 6;
        const int endHour = 23;
        const double hourHeight = 52;

        for (int h = startHour; h <= endHour; h++)
            WeekTimeGrid.RowDefinitions.Add(new RowDefinition(new GridLength(hourHeight)));

        // Add the gutter column + 7 day columns
        WeekTimeGrid.ColumnDefinitions.Clear();
        WeekTimeGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
        for (int i = 0; i < 7; i++)
            WeekTimeGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int h = startHour; h <= endHour; h++)
        {
            var row = h - startHour;

            // Hour label
            WeekTimeGrid.Add(new Label
            {
                Text = $"{h:00}",
                FontSize = 10,
                TextColor = Color.FromArgb("#44444F"),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 2, 4, 0),
            }, 0, row);

            // Horizontal divider across all columns
            for (int col = 1; col <= 7; col++)
            {
                WeekTimeGrid.Add(new BoxView
                {
                    HeightRequest = 1,
                    Color = Color.FromArgb("#2A2A35"),
                    VerticalOptions = LayoutOptions.Start,
                }, col, row);
            }
        }

        // Place events
        for (int d = 0; d < 7; d++)
        {
            var day = weekStart.AddDays(d);
            var dayEvents = _events
                .Where(e => e.StartAt.Date == day.Date && !e.IsAllDay)
                .OrderBy(e => e.StartAt)
                .ToList();

            foreach (var evt in dayEvents)
            {
                int hour = evt.StartAt.Hour;
                if (hour < startHour || hour > endHour) continue;
                int row = hour - startHour;

                var chip = BuildEventChip(evt, compact: true);
                WeekTimeGrid.Add(chip, d + 1, row);
            }
        }
    }

    private void RenderDayView()
    {
        DayViewTitle.Text = _selectedDay.ToString("dddd, d MMMM");
        DayTimeGrid.Children.Clear();
        DayTimeGrid.RowDefinitions.Clear();

        const int startHour = 6;
        const int endHour = 23;
        const double hourHeight = 60;

        DayTimeGrid.ColumnDefinitions.Clear();
        DayTimeGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(48)));
        DayTimeGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int h = startHour; h <= endHour; h++)
            DayTimeGrid.RowDefinitions.Add(new RowDefinition(new GridLength(hourHeight)));

        var now = DateTime.Now;
        bool isToday = _selectedDay.Date == DateTime.Today;

        for (int h = startHour; h <= endHour; h++)
        {
            var row = h - startHour;

            // Hour label
            DayTimeGrid.Add(new Label
            {
                Text = $"{h:00}:00",
                FontSize = 11,
                TextColor = Color.FromArgb("#44444F"),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 2, 8, 0),
            }, 0, row);

            // Horizontal line
            DayTimeGrid.Add(new BoxView
            {
                HeightRequest = 1,
                Color = Color.FromArgb("#2A2A35"),
                VerticalOptions = LayoutOptions.Start,
            }, 1, row);

            // Current time indicator
            if (isToday && now.Hour == h)
            {
                var minuteFrac = now.Minute / 60.0;
                var timeLine = new BoxView
                {
                    HeightRequest = 2,
                    Color = Color.FromArgb("#FF5C5C"),
                    VerticalOptions = LayoutOptions.Start,
                    Margin = new Thickness(0, (minuteFrac * hourHeight) - 1, 0, 0),
                };
                DayTimeGrid.Add(timeLine, 1, row);
            }
        }

        // Place events for the day
        var dayEvents = _events
            .Where(e => e.StartAt.Date == _selectedDay.Date && !e.IsAllDay)
            .OrderBy(e => e.StartAt)
            .ToList();

        foreach (var evt in dayEvents)
        {
            int hour = evt.StartAt.Hour;
            if (hour < startHour || hour > endHour) continue;
            int row = hour - startHour;

            var chip = BuildEventChip(evt, compact: false);
            DayTimeGrid.Add(chip, 1, row);
        }

        // Scroll to business hours
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(100);
            await DayScrollView.ScrollToAsync(0, (8 - startHour) * hourHeight, false);
        });
    }

    private static View BuildEventChip(CalendarEvent evt, bool compact)
    {
        var border = new Border
        {
            Margin = new Thickness(2, 2, 2, 0),
            Padding = new Thickness(6, 3),
            BackgroundColor = evt.EventColor.WithAlpha(0.2f),
            Stroke = evt.EventColor,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
        };

        var content = new VerticalStackLayout { Spacing = 1 };
        content.Children.Add(new Label
        {
            Text = evt.Title,
            FontSize = compact ? 10 : 12,
            TextColor = evt.EventColor,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = compact ? 1 : 2,
        });
        if (!compact)
        {
            content.Children.Add(new Label
            {
                Text = evt.TimeLabel,
                FontSize = 10,
                TextColor = Color.FromArgb("#66667A"),
            });
        }
        border.Content = content;
        return border;
    }

    // ── Day detail sheet ──────────────────────────────────────────────────

    private DateTime _sheetDay;

    private void ShowDayDetail(DateTime date, List<CalendarEvent> events)
    {
        _sheetDay = date;
        DayDetailTitle.Text = date.ToString("dddd, d MMMM");
        DayDetailList.Children.Clear();

        if (events.Count == 0)
        {
            DayDetailList.Children.Add(new Label
            {
                Text = "No events for this day",
                Style = (Style)Resources["ProductivityCaption"],
                HorizontalOptions = LayoutOptions.Center,
            });
        }

        foreach (var evt in events)
        {
            var card = new Border
            {
                Padding = new Thickness(12, 8),
                BackgroundColor = evt.EventColor.WithAlpha(0.1f),
                Stroke = evt.EventColor.WithAlpha(0.4f),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            };

            var inner = new Grid { ColumnDefinitions = new ColumnDefinitionCollection(new[]
            {
                new ColumnDefinition(new GridLength(4)),
                new ColumnDefinition(GridLength.Star),
            }), ColumnSpacing = 8 };

            inner.Add(new BoxView
            {
                Color = evt.EventColor,
                WidthRequest = 4,
                VerticalOptions = LayoutOptions.Fill,
                CornerRadius = 2,
            }, 0, 0);

            var info = new VerticalStackLayout { Spacing = 2 };
            info.Children.Add(new Label
            {
                Text = evt.Title,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#E0E0F0"),
            });
            info.Children.Add(new Label
            {
                Text = $"{evt.SourceIcon} {evt.TimeLabel}",
                FontSize = 11,
                TextColor = Color.FromArgb("#66667A"),
            });
            inner.Add(info, 1, 0);
            card.Content = inner;

            DayDetailList.Children.Add(card);
        }

        DayDetailOverlay.IsVisible = true;
    }

    private void OnOverlayTapped(object? sender, TappedEventArgs e) =>
        DayDetailOverlay.IsVisible = false;

    private async void OnAddTaskForDayClicked(object? sender, EventArgs e)
    {
        DayDetailOverlay.IsVisible = false;

        var title = await DisplayPromptAsync(
            "Add task",
            $"Task for {_sheetDay:MMM d}:",
            "Add", "Cancel",
            placeholder: "e.g. Finish report");
        if (string.IsNullOrWhiteSpace(title)) return;

        await GetTaskService().CreateTaskAsync(title.Trim(), "medium", _sheetDay);
        await RefreshAsync();
    }

    // ── Sync status ───────────────────────────────────────────────────────

    private async Task UpdateSyncStatusAsync()
    {
        try
        {
            var status = await GetCalendarService().RefreshStatusAsync();
            if (status == null || !status.Connected)
            {
                SyncStatusLabel.Text = "●";
                SyncStatusLabel.TextColor = Color.FromArgb("#44444F");
                ToolTipProperties.SetText(SyncStatusLabel, "Google Calendar not connected");
            }
            else
            {
                SyncStatusLabel.Text = "●";
                SyncStatusLabel.TextColor = Color.FromArgb("#4ECCA3");
                var last = status.LastSyncedAt != null ? $"Synced {status.LastSyncedAt}" : "Connected";
                ToolTipProperties.SetText(SyncStatusLabel, last);
            }
        }
        catch
        {
            // Ignore
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static DateTime GetWeekStart(DateTime d) =>
        d.AddDays(-(int)d.DayOfWeek);

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
