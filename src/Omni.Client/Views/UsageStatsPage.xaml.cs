using System.Collections.ObjectModel;
using Omni.Client.Controls;
using Omni.Client.Models.Usage;
using Omni.Client.Presentation.ViewModels;

namespace Omni.Client;

public partial class UsageStatsPage : ContentPage
{
    private readonly UsageStatsViewModel _vm;
    private readonly UsagePieDrawable _pieDrawable = new();
    private readonly UsageBarDrawable _barDrawable = new();
    private readonly FocusScoreTrendDrawable _trendDrawable = new();
    private readonly ActivityHeatmapDrawable _heatmapDrawable = new();
    private readonly WeeklyFocusBarDrawable _weeklyFocusDrawable = new();
    private readonly ObservableCollection<string> _categoryOptions = new() { "All" };
    private readonly ObservableCollection<string> _appOptions = new() { "All" };
    private bool _ignorePickerChanges;

    public UsageStatsPage(UsageStatsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        CategoryPicker.ItemsSource = _categoryOptions;
        CategoryPicker.SelectedIndex = 0;
        AppPicker.ItemsSource = _appOptions;
        AppPicker.SelectedIndex = 0;

        PieChartView.Drawable = _pieDrawable;
        BarChartView.Drawable = _barDrawable;
        TrendChartView.Drawable = _trendDrawable;
        HeatmapView.Drawable = _heatmapDrawable;
        WeeklyFocusView.Drawable = _weeklyFocusDrawable;

        UpdatePeriodButtons();
        StatsNetworkBanner.RetryAction = () => _ = _vm.LoadCommand.ExecuteAsync(null);

        _vm.DataLoaded += OnDataLoaded;
        _vm.LoadFailed += OnLoadFailed;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Today changes quickly and must not reuse a stale empty load; other periods use a short TTL.
        if (string.Equals(_vm.SelectedPeriod, "Today", StringComparison.Ordinal) ||
            _vm.IsDataStale(TimeSpan.FromSeconds(60)))
            _ = _vm.LoadCommand.ExecuteAsync(null);
    }

    private void OnDataLoaded()
    {
        var entries = _vm.UsageData?.Entries ?? new List<UsageListEntry>();
        UpdateDropdowns(entries);
        UpdateCharts(entries);
        UpdateNewCharts(entries);
        _ = StatsNetworkBanner.HideBannerAsync();
    }

    private void OnLoadFailed(string message)
    {
        ChartsFrame.IsVisible = false;
        _ = StatsNetworkBanner.ShowBannerAsync("Can't load stats", "Pull to retry when back online.");
    }

    private void OnPeriodTodayClicked(object? sender, EventArgs e)
    {
        _vm.SelectPeriod("Today");
        UpdatePeriodButtons();
        _ = _vm.SwitchPeriodCommand.ExecuteAsync(null);
    }

    private void OnPeriodWeekClicked(object? sender, EventArgs e)
    {
        _vm.SelectPeriod("Week");
        UpdatePeriodButtons();
        _ = _vm.SwitchPeriodCommand.ExecuteAsync(null);
    }

    private void OnPeriodMonthClicked(object? sender, EventArgs e)
    {
        _vm.SelectPeriod("Month");
        UpdatePeriodButtons();
        _ = _vm.SwitchPeriodCommand.ExecuteAsync(null);
    }

    private void UpdatePeriodButtons()
    {
        Style? ctaStyle = null;
        Style? secStyle = null;
        try
        {
            ctaStyle = (Style)Application.Current!.Resources["ProductivityPillButton"];
            secStyle = (Style)Application.Current!.Resources["ProductivitySegmentButton"];
        }
        catch { }
        if (ctaStyle == null || secStyle == null) return;
        PeriodTodayBtn.Style = _vm.SelectedPeriod == "Today" ? ctaStyle : secStyle;
        PeriodWeekBtn.Style = _vm.SelectedPeriod == "Week" ? ctaStyle : secStyle;
        PeriodMonthBtn.Style = _vm.SelectedPeriod == "Month" ? ctaStyle : secStyle;
    }

    private void OnCategoryChanged(object? sender, EventArgs e)
    {
        if (_ignorePickerChanges || CategoryPicker.SelectedIndex < 0) return;
        var cat = CategoryPicker.SelectedIndex <= 0 ? null : CategoryPicker.SelectedItem?.ToString();
        _vm.SelectedCategory = cat == "All" ? null : cat;
        _ = _vm.LoadCommand.ExecuteAsync(null);
    }

    private void OnAppFilterChanged(object? sender, EventArgs e)
    {
        if (_ignorePickerChanges || AppPicker.SelectedIndex < 0) return;
        var app = AppPicker.SelectedIndex <= 0 ? null : AppPicker.SelectedItem?.ToString();
        _vm.SelectedApp = app == "All" ? null : app;
        _ = _vm.LoadCommand.ExecuteAsync(null);
    }

    private void UpdateDropdowns(List<UsageListEntry> entries)
    {
        var categories = entries
            .Select(e => e.Category ?? "").Where(s => !string.IsNullOrEmpty(s))
            .Distinct().OrderBy(s => s).ToList();
        var apps = entries
            .Select(e => e.AppName ?? "").Where(s => !string.IsNullOrEmpty(s))
            .Distinct().OrderBy(s => s).ToList();

        _ignorePickerChanges = true;
        try
        {
            _categoryOptions.Clear();
            _categoryOptions.Add("All");
            foreach (var c in categories) _categoryOptions.Add(c);

            _appOptions.Clear();
            _appOptions.Add("All");
            foreach (var a in apps) _appOptions.Add(a);

            if (CategoryPicker.SelectedIndex >= _categoryOptions.Count) CategoryPicker.SelectedIndex = 0;
            if (AppPicker.SelectedIndex >= _appOptions.Count) AppPicker.SelectedIndex = 0;
        }
        finally { _ignorePickerChanges = false; }
    }

    private void UpdateCharts(List<UsageListEntry> entries)
    {
        if (entries.Count == 0)
        {
            ChartsFrame.IsVisible = false;
            return;
        }

        var byCategory = entries
            .GroupBy(e => e.Category ?? "Other")
            .Select(g => new ChartSegment { Label = g.Key, Value = g.Sum(e => e.TotalSeconds) })
            .Where(s => s.Value > 0)
            .OrderByDescending(s => s.Value)
            .Take(8)
            .ToList();
        _pieDrawable.Segments = byCategory;
        _pieDrawable.Total = byCategory.Sum(s => s.Value);

        var byApp = entries
            .GroupBy(e => e.AppName ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new ChartSegment { Label = g.Key, Value = g.Sum(e => e.TotalSeconds) })
            .Where(s => s.Value > 0)
            .OrderByDescending(s => s.Value)
            .Take(8)
            .ToList();
        _barDrawable.Segments = byApp;
        _barDrawable.MaxValue = byApp.Count > 0 ? byApp.Max(s => s.Value) : 0;

        ChartsFrame.IsVisible = byCategory.Count > 0 || byApp.Count > 0;
        PieChartView.Invalidate();
        BarChartView.Invalidate();
    }

    private static bool IsFocusCategory(string? c) =>
        string.Equals(c, "Coding", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(c, "Productivity", StringComparison.OrdinalIgnoreCase);

    private void UpdateNewCharts(List<UsageListEntry> entries)
    {
        var dailyFocus = entries
            .Where(e => IsFocusCategory(e.Category) && DateTime.TryParse(e.Date, out _))
            .GroupBy(e => DateTime.Parse(e.Date).ToString("yyyy-MM-dd"))
            .Select(g => (Date: g.Key, Score: Math.Min(100, (int)(g.Sum(x => x.TotalSeconds) / 36))))
            .OrderBy(x => x.Date)
            .ToList();
        _trendDrawable.SetData(dailyFocus);
        TrendChartView.Invalidate();

        var heatmapData = entries
            .Where(e => IsFocusCategory(e.Category) && DateTime.TryParse(e.Date, out _))
            .GroupBy(e => DateTime.Parse(e.Date).ToString("yyyy-MM-dd"))
            .Select(g => (Date: g.Key, FocusMinutes: (int)(g.Sum(x => x.TotalSeconds) / 60)));
        _heatmapDrawable.SetData(heatmapData);
        HeatmapView.Invalidate();

        // Weekday focus bars: Mon=0 … Sun=6
        var weekdaySeconds = new long[7];
        foreach (var e in entries)
        {
            if (!IsFocusCategory(e.Category) || !DateTime.TryParse(e.Date, out var dt)) continue;
            weekdaySeconds[((int)dt.DayOfWeek + 6) % 7] += e.TotalSeconds;
        }
        int todayIdx = ((int)DateTime.Today.DayOfWeek + 6) % 7;
        _weeklyFocusDrawable.SetData(weekdaySeconds, todayIdx);
        WeeklyFocusView.Invalidate();
    }
}
