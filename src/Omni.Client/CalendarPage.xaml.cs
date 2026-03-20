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

    // ── App resource helpers ───────────────────────────────────────────────
    private static Style AppStyle(string key) =>
        (Style)Application.Current!.Resources[key];

    public CalendarPage()
    {
        InitializeComponent();
        _displayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _selectedDay = DateTime.Today;

        // Seed create-event form defaults
        EventDatePicker.Date = DateTime.Today;
        EventStartTime.Time = new TimeSpan(DateTime.Now.Hour + 1, 0, 0);
        EventEndTime.Time   = new TimeSpan(DateTime.Now.Hour + 2, 0, 0);
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
        WeekdayHeader.IsVisible = _viewMode == ViewMode.Month;
    }

    private async void OnPrevClicked(object? sender, EventArgs e)
    {
        switch (_viewMode)
        {
            case ViewMode.Month: _displayedMonth = _displayedMonth.AddMonths(-1); break;
            case ViewMode.Week:  _selectedDay = _selectedDay.AddDays(-7);         break;
            case ViewMode.Day:   _selectedDay = _selectedDay.AddDays(-1);         break;
        }
        await RefreshAsync();
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        switch (_viewMode)
        {
            case ViewMode.Month: _displayedMonth = _displayedMonth.AddMonths(1); break;
            case ViewMode.Week:  _selectedDay = _selectedDay.AddDays(7);         break;
            case ViewMode.Day:   _selectedDay = _selectedDay.AddDays(1);         break;
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
        MonthViewBtn.Style = _viewMode == ViewMode.Month ? AppStyle("ProductivityPillButton") : AppStyle("ProductivitySecondaryButton");
        WeekViewBtn.Style  = _viewMode == ViewMode.Week  ? AppStyle("ProductivityPillButton") : AppStyle("ProductivitySecondaryButton");
        DayViewBtn.Style   = _viewMode == ViewMode.Day   ? AppStyle("ProductivityPillButton") : AppStyle("ProductivitySecondaryButton");

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

    private (DateTime start, DateTime end) GetCurrentDateRange()
    {
        switch (_viewMode)
        {
            case ViewMode.Week:
                return (GetWeekStart(_selectedDay), GetWeekStart(_selectedDay).AddDays(7));
            case ViewMode.Day:
                return (_selectedDay.Date, _selectedDay.Date.AddDays(1));
            default:
                // Expand range to cover the full 6-week grid (days from prev/next month)
                var gridStart = _displayedMonth.AddDays(-(int)_displayedMonth.DayOfWeek);
                var gridEnd   = gridStart.AddDays(42);
                return (gridStart, gridEnd);
        }
    }

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

    // ── Month View ────────────────────────────────────────────────────────

    private void RenderMonthView()
    {
        MonthGridContainer.Children.Clear();

        var firstDay     = _displayedMonth;
        var startOfGrid  = firstDay.AddDays(-(int)firstDay.DayOfWeek);

        for (int week = 0; week < 6; week++)
        {
            var weekRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(
                    Enumerable.Repeat(new ColumnDefinition(GridLength.Star), 7).ToArray()),
                ColumnSpacing = 3,
            };

            for (int dow = 0; dow < 7; dow++)
            {
                var date = startOfGrid.AddDays(week * 7 + dow);
                var isCurrentMonth = date.Month == _displayedMonth.Month;
                var isToday = date.Date == DateTime.Today;
                var dayEvents = _events.Where(e => e.StartAt.Date == date.Date).ToList();

                weekRow.Add(BuildDayCell(date, isCurrentMonth, isToday, dayEvents), dow, 0);
            }

            MonthGridContainer.Children.Add(weekRow);
        }
    }

    private View BuildDayCell(DateTime date, bool isCurrentMonth, bool isToday, List<CalendarEvent> dayEvents)
    {
        var cellBg = isCurrentMonth ? Color.FromArgb("#1A1A1F") : Color.FromArgb("#13131A");

        var border = new Border
        {
            Padding = new Thickness(5, 5, 5, 6),
            BackgroundColor = cellBg,
            Stroke = isToday ? Color.FromArgb("#7C6AF7") : Color.FromArgb("#22222E"),
            StrokeThickness = isToday ? 1.5 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            HeightRequest = 82,
        };

        var content = new VerticalStackLayout { Spacing = 3 };

        // Day number — Apple-style filled circle for today
        var dayNumContainer = new Grid
        {
            WidthRequest = 24, HeightRequest = 24,
            HorizontalOptions = LayoutOptions.Start,
        };
        if (isToday)
        {
            dayNumContainer.Children.Add(new Border
            {
                WidthRequest = 24, HeightRequest = 24,
                BackgroundColor = Color.FromArgb("#7C6AF7"),
                Stroke = Colors.Transparent,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            });
        }
        dayNumContainer.Children.Add(new Label
        {
            Text = date.Day.ToString(),
            FontSize = 12,
            FontAttributes = isToday ? FontAttributes.Bold : FontAttributes.None,
            TextColor = isToday
                ? Colors.White
                : isCurrentMonth ? Color.FromArgb("#E0E0F0") : Color.FromArgb("#44444F"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center,
        });
        content.Children.Add(dayNumContainer);

        // Event pills (up to 2, then "+N more")
        int shown = 0;
        foreach (var evt in dayEvents)
        {
            if (shown >= 2) break;
            content.Children.Add(BuildEventPill(evt));
            shown++;
        }
        if (dayEvents.Count > 2)
        {
            content.Children.Add(new Label
            {
                Text = $"+{dayEvents.Count - 2} more",
                FontSize = 9,
                TextColor = Color.FromArgb("#66667A"),
            });
        }

        border.Content = content;
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => ShowDayDetail(date, dayEvents))
        });

        return border;
    }

    private static View BuildEventPill(CalendarEvent evt)
    {
        return new Border
        {
            Padding = new Thickness(4, 2),
            BackgroundColor = evt.EventColor.WithAlpha(0.22f),
            Stroke = Colors.Transparent,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            Content = new Label
            {
                Text = evt.Title,
                FontSize = 9,
                TextColor = evt.EventColor,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
            },
        };
    }

    // ── Week View ─────────────────────────────────────────────────────────

    private void RenderWeekView()
    {
        var weekStart = GetWeekStart(_selectedDay);

        // ── Day headers ──────────────────────────────────────────────────
        WeekDayHeaders.Children.Clear();
        WeekDayHeaders.ColumnDefinitions.Clear();
        WeekDayHeaders.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(44)));
        for (int i = 0; i < 7; i++)
            WeekDayHeaders.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        WeekDayHeaders.Add(new Label { Text = "", FontSize = 10 }, 0, 0);

        for (int d = 0; d < 7; d++)
        {
            var day     = weekStart.AddDays(d);
            var isToday = day.Date == DateTime.Today;

            var stack = new VerticalStackLayout { Spacing = 1, HorizontalOptions = LayoutOptions.Center };
            stack.Children.Add(new Label
            {
                Text = day.ToString("ddd").ToUpper(),
                FontSize = 9,
                TextColor = isToday ? Color.FromArgb("#7C6AF7") : Color.FromArgb("#66667A"),
                HorizontalOptions = LayoutOptions.Center,
            });

            var numBorder = new Border
            {
                WidthRequest = 28, HeightRequest = 28,
                BackgroundColor = isToday ? Color.FromArgb("#7C6AF7") : Colors.Transparent,
                Stroke = Colors.Transparent,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                Content = new Label
                {
                    Text = day.Day.ToString(),
                    FontSize = 14,
                    FontAttributes = isToday ? FontAttributes.Bold : FontAttributes.None,
                    TextColor = isToday ? Colors.White : Color.FromArgb("#E0E0F0"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions   = LayoutOptions.Center,
                },
            };
            stack.Children.Add(numBorder);
            WeekDayHeaders.Add(stack, d + 1, 0);
        }

        // ── All-day events strip ─────────────────────────────────────────
        WeekAllDayStrip.Children.Clear();
        WeekAllDayStrip.ColumnDefinitions.Clear();
        WeekAllDayStrip.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(44)));
        for (int i = 0; i < 7; i++)
            WeekAllDayStrip.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        bool hasAllDay = false;
        for (int d = 0; d < 7; d++)
        {
            var day = weekStart.AddDays(d);
            var allDays = _events.Where(e => e.IsAllDay && e.StartAt.Date == day.Date).ToList();
            foreach (var evt in allDays)
            {
                hasAllDay = true;
                WeekAllDayStrip.Add(BuildAllDayChip(evt), d + 1, 0);
            }
        }
        WeekAllDayStrip.IsVisible = hasAllDay;

        // ── Time grid ────────────────────────────────────────────────────
        WeekTimeGrid.Children.Clear();
        WeekTimeGrid.RowDefinitions.Clear();
        WeekTimeGrid.ColumnDefinitions.Clear();
        WeekTimeGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(44)));
        for (int i = 0; i < 7; i++)
            WeekTimeGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        const int startHour  = 6;
        const int endHour    = 23;
        const double hourH   = 54;

        for (int h = startHour; h <= endHour; h++)
            WeekTimeGrid.RowDefinitions.Add(new RowDefinition(new GridLength(hourH)));

        for (int h = startHour; h <= endHour; h++)
        {
            int row = h - startHour;

            WeekTimeGrid.Add(new Label
            {
                Text = $"{h:00}",
                FontSize = 10,
                TextColor = Color.FromArgb("#44444F"),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Start,
                Margin = new Thickness(0, 2, 6, 0),
            }, 0, row);

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

        // Place timed events with duration-aware height
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
                if (hour < startHour || hour >= endHour) continue;

                int row = hour - startHour;
                double topOffset = (evt.StartAt.Minute / 60.0) * hourH;

                double durationMins = evt.EndAt.HasValue
                    ? (evt.EndAt.Value - evt.StartAt).TotalMinutes
                    : 60;
                double chipH = Math.Max(20, (durationMins / 60.0) * hourH);

                var chip = BuildEventBlock(evt, chipH, compact: true);
                chip.Margin = new Thickness(2, topOffset, 2, 0);
                chip.VerticalOptions = LayoutOptions.Start;

                WeekTimeGrid.Add(chip, d + 1, row);
            }
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(80);
            await WeekScrollView.ScrollToAsync(0, (8 - startHour) * hourH, false);
        });
    }

    // ── Day View ──────────────────────────────────────────────────────────

    private void RenderDayView()
    {
        DayViewTitle.Text = _selectedDay.ToString("dddd, d MMMM");

        // ── All-day events strip ──────────────────────────────────────────
        DayAllDayStrip.Children.Clear();
        var allDayEvents = _events.Where(e => e.IsAllDay && e.StartAt.Date == _selectedDay.Date).ToList();
        foreach (var evt in allDayEvents)
            DayAllDayStrip.Children.Add(BuildAllDayChip(evt));
        DayAllDayStrip.IsVisible = allDayEvents.Count > 0;

        // ── Time grid ────────────────────────────────────────────────────
        DayTimeGrid.Children.Clear();
        DayTimeGrid.RowDefinitions.Clear();
        DayTimeGrid.ColumnDefinitions.Clear();
        DayTimeGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(52)));
        DayTimeGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        const int startHour  = 6;
        const int endHour    = 23;
        const double hourH   = 64;

        for (int h = startHour; h <= endHour; h++)
            DayTimeGrid.RowDefinitions.Add(new RowDefinition(new GridLength(hourH)));

        bool isToday = _selectedDay.Date == DateTime.Today;
        var now = DateTime.Now;

        for (int h = startHour; h <= endHour; h++)
        {
            int row = h - startHour;

            DayTimeGrid.Add(new Label
            {
                Text = $"{h:00}:00",
                FontSize = 11,
                TextColor = Color.FromArgb("#44444F"),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Start,
                Margin = new Thickness(0, 2, 10, 0),
            }, 0, row);

            DayTimeGrid.Add(new BoxView
            {
                HeightRequest = 1,
                Color = Color.FromArgb("#2A2A35"),
                VerticalOptions = LayoutOptions.Start,
            }, 1, row);

            // Current time indicator
            if (isToday && now.Hour == h)
            {
                double minuteFrac = now.Minute / 60.0;
                DayTimeGrid.Add(new BoxView
                {
                    HeightRequest = 2,
                    Color = Color.FromArgb("#FF5C5C"),
                    VerticalOptions = LayoutOptions.Start,
                    Margin = new Thickness(0, minuteFrac * hourH - 1, 0, 0),
                }, 1, row);
            }
        }

        // Place timed events
        var timedEvents = _events
            .Where(e => e.StartAt.Date == _selectedDay.Date && !e.IsAllDay)
            .OrderBy(e => e.StartAt)
            .ToList();

        foreach (var evt in timedEvents)
        {
            int hour = evt.StartAt.Hour;
            if (hour < startHour || hour >= endHour) continue;

            int row = hour - startHour;
            double topOffset = (evt.StartAt.Minute / 60.0) * hourH;

            double durationMins = evt.EndAt.HasValue
                ? (evt.EndAt.Value - evt.StartAt).TotalMinutes
                : 60;
            double chipH = Math.Max(24, (durationMins / 60.0) * hourH);

            var chip = BuildEventBlock(evt, chipH, compact: false);
            chip.Margin = new Thickness(4, topOffset, 4, 0);
            chip.VerticalOptions = LayoutOptions.Start;

            DayTimeGrid.Add(chip, 1, row);
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(80);
            await DayScrollView.ScrollToAsync(0, (8 - startHour) * hourH, false);
        });
    }

    // ── Event block builders ──────────────────────────────────────────────

    private View BuildEventBlock(CalendarEvent evt, double height, bool compact)
    {
        var border = new Border
        {
            HeightRequest = height,
            Padding = new Thickness(6, 3),
            BackgroundColor = evt.EventColor.WithAlpha(0.2f),
            Stroke = evt.EventColor.WithAlpha(0.7f),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
        };

        var content = new VerticalStackLayout { Spacing = 1 };
        content.Children.Add(new Label
        {
            Text = evt.Title,
            FontSize = compact ? 10 : 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = evt.EventColor,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = compact ? 1 : 2,
        });
        if (!compact && height >= 40)
        {
            content.Children.Add(new Label
            {
                Text = evt.TimeLabel,
                FontSize = 10,
                TextColor = Color.FromArgb("#66667A"),
            });
        }
        border.Content = content;

        // Tap → show detail sheet for this single event
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                _sheetDay = evt.StartAt.Date;
                ShowDayDetail(evt.StartAt.Date, new List<CalendarEvent> { evt });
            })
        });

        return border;
    }

    private static View BuildAllDayChip(CalendarEvent evt)
    {
        return new Border
        {
            Margin = new Thickness(2, 1),
            Padding = new Thickness(8, 3),
            BackgroundColor = evt.EventColor.WithAlpha(0.2f),
            Stroke = evt.EventColor.WithAlpha(0.5f),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            Content = new Label
            {
                Text = evt.Title,
                FontSize = 11,
                TextColor = evt.EventColor,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
            },
        };
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
                Style = AppStyle("ProductivityCaption"),
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 8),
            });
        }

        foreach (var evt in events)
        {
            var card = new Border
            {
                Padding = new Thickness(12, 10),
                BackgroundColor = evt.EventColor.WithAlpha(0.08f),
                Stroke = evt.EventColor.WithAlpha(0.3f),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            };

            var inner = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(
                    new ColumnDefinition(new GridLength(4)),
                    new ColumnDefinition(GridLength.Star)),
                ColumnSpacing = 10,
            };

            inner.Add(new BoxView
            {
                Color = evt.EventColor,
                WidthRequest = 4,
                VerticalOptions = LayoutOptions.Fill,
                CornerRadius = 2,
            }, 0, 0);

            var info = new VerticalStackLayout { Spacing = 3 };
            info.Children.Add(new Label
            {
                Text = evt.Title,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#E0E0F0"),
            });
            info.Children.Add(new Label
            {
                Text = $"{evt.SourceIcon}  {evt.TimeLabel}",
                FontSize = 11,
                TextColor = Color.FromArgb("#66667A"),
            });
            if (!string.IsNullOrEmpty(evt.Description))
            {
                info.Children.Add(new Label
                {
                    Text = evt.Description,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#9898A8"),
                    MaxLines = 2,
                    LineBreakMode = LineBreakMode.TailTruncation,
                });
            }
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
        OpenCreateEventSheet(_sheetDay);
        await Task.CompletedTask;
    }

    // ── FAB / Create event sheet ──────────────────────────────────────────

    private void OnFabTapped(object? sender, TappedEventArgs e) =>
        OpenCreateEventSheet(_selectedDay);

    private void OpenCreateEventSheet(DateTime forDate)
    {
        EventTitleEntry.Text = string.Empty;
        EventDatePicker.Date = forDate;
        AllDaySwitch.IsToggled = false;
        EventStartTime.Time = new TimeSpan(9, 0, 0);
        EventEndTime.Time   = new TimeSpan(10, 0, 0);
        EventNotesEditor.Text = string.Empty;
        TimePickerRow.IsVisible = true;

        // Populate calendar type picker
        CalendarTypePicker.Items.Clear();
        CalendarTypePicker.Items.Add("Omni Task");
        if (GetCalendarService().IsConnected)
            CalendarTypePicker.Items.Add("Google Calendar");
        CalendarTypePicker.SelectedIndex = 0;

        CreateEventOverlay.IsVisible = true;
    }

    private void OnCreateEventOverlayTapped(object? sender, TappedEventArgs e)
    {
        // Only dismiss if tapping the backdrop, not the sheet
        CreateEventOverlay.IsVisible = false;
    }

    private void OnCreateEventCancel(object? sender, EventArgs e) =>
        CreateEventOverlay.IsVisible = false;

    private void OnAllDayToggled(object? sender, ToggledEventArgs e) =>
        TimePickerRow.IsVisible = !e.Value;

    private async void OnCreateEventSave(object? sender, EventArgs e)
    {
        var title = EventTitleEntry.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            await DisplayAlertAsync("Required", "Please enter an event title.", "OK");
            return;
        }

        CreateEventOverlay.IsVisible = false;

        var date     = (EventDatePicker.Date ?? DateTime.Today).Date;
        var isAllDay = AllDaySwitch.IsToggled;
        var useGoogle = CalendarTypePicker.SelectedItem?.ToString() == "Google Calendar";

        DateTime start;
        DateTime? end;
        if (isAllDay)
        {
            start = date;
            end   = null;
        }
        else
        {
            start = date + (EventStartTime.Time ?? TimeSpan.FromHours(9));
            end   = date + (EventEndTime.Time   ?? TimeSpan.FromHours(10));
            if (end <= start) end = start.AddHours(1);
        }

        bool success;
        if (useGoogle)
        {
            success = await GetCalendarService().CreateGoogleEventAsync(
                title, start, end, isAllDay,
                EventNotesEditor.Text?.Trim());
        }
        else
        {
            try
            {
                await GetTaskService().CreateTaskAsync(title, "medium", start);
                success = true;
            }
            catch
            {
                success = false;
            }
        }

        if (!success)
            await DisplayAlertAsync("Error", "Could not save the event. Please try again.", "OK");

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
                SyncDotLabel.TextColor  = Color.FromArgb("#44444F");
                SyncTextLabel.Text      = "Connect Cal";
                SyncTextLabel.TextColor = Color.FromArgb("#66667A");
            }
            else
            {
                SyncDotLabel.TextColor  = Color.FromArgb("#4ECCA3");
                var email = status.Email?.Split('@')[0] ?? "Google";
                SyncTextLabel.Text      = email.Length > 10 ? email[..10] + "…" : email;
                SyncTextLabel.TextColor = Color.FromArgb("#9898A8");
            }
        }
        catch
        {
            // ignore
        }
    }

    private async void OnSyncStatusTapped(object? sender, TappedEventArgs e)
    {
        if (!GetCalendarService().IsConnected)
        {
            // Navigate to account page to connect
            await Shell.Current.GoToAsync(nameof(AccountPage));
            return;
        }

        // Trigger manual sync
        SyncTextLabel.Text = "Syncing…";
        var ok = await GetCalendarService().SyncAsync();
        if (ok)
        {
            await RefreshAsync();
        }
        else
        {
            SyncTextLabel.Text = "Sync failed";
            await Task.Delay(2000);
            await UpdateSyncStatusAsync();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static DateTime GetWeekStart(DateTime d) => d.AddDays(-(int)d.DayOfWeek);

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
