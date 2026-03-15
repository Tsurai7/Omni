using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Omni.Client.Abstractions;
using Omni.Client.Controls;
using Omni.Client.Models.Usage;

namespace Omni.Client;

public partial class UsageStatsPage : ContentPage, INotifyPropertyChanged
{
    private IUsageService? _usageService;
    private bool _isRefreshing;
    private string _emptyView = "Pull to load synced usage.";
    private ObservableCollection<UsageDateGroup> _groupedEntries = new();
    private readonly List<string> _viewOptions = new() { "By day", "By week", "By month", "By category", "By app" };
    private readonly ObservableCollection<string> _categoryOptions = new() { "All" };
    private readonly ObservableCollection<string> _appOptions = new() { "All" };
    private bool _ignorePickerChanges;
    private readonly UsagePieDrawable _pieDrawable = new();
    private readonly UsageBarDrawable _barDrawable = new();

    private string _insightBiggestDistraction = "";
    private string _insightFocusThisWeek = "";
    private string _insightTrend = "";
    private string _recommendationChipText = "";

    public UsageStatsPage()
    {
        InitializeComponent();
        BindingContext = this;
        RefreshCommand = new Command(async () => await LoadAsync());
        ViewPicker.ItemsSource = _viewOptions;
        ViewPicker.SelectedIndex = 0;
        CategoryPicker.ItemsSource = _categoryOptions;
        CategoryPicker.SelectedIndex = 0;
        AppPicker.ItemsSource = _appOptions;
        AppPicker.SelectedIndex = 0;
        PieChartView.Drawable = _pieDrawable;
        BarChartView.Drawable = _barDrawable;
    }

    private IUsageService GetUsageService()
    {
        if (_usageService == null)
            _usageService = MauiProgram.AppServices?.GetService<IUsageService>();
        return _usageService!;
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

    public ObservableCollection<UsageDateGroup> GroupedEntries
    {
        get => _groupedEntries;
        set { _groupedEntries = value; OnPropertyChanged(); }
    }

    public string InsightBiggestDistraction
    {
        get => _insightBiggestDistraction;
        set { if (_insightBiggestDistraction != value) { _insightBiggestDistraction = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasInsights)); } }
    }

    public string InsightFocusThisWeek
    {
        get => _insightFocusThisWeek;
        set { if (_insightFocusThisWeek != value) { _insightFocusThisWeek = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasInsights)); } }
    }

    public string InsightTrend
    {
        get => _insightTrend;
        set { if (_insightTrend != value) { _insightTrend = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasInsights)); } }
    }

    public string RecommendationChipText
    {
        get => _recommendationChipText;
        set { if (_recommendationChipText != value) { _recommendationChipText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRecommendation)); } }
    }

    public bool HasInsights => !string.IsNullOrEmpty(InsightBiggestDistraction) || !string.IsNullOrEmpty(InsightFocusThisWeek) || !string.IsNullOrEmpty(InsightTrend);
    public bool HasRecommendation => !string.IsNullOrEmpty(RecommendationChipText);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var auth = MauiProgram.AppServices?.GetService<IAuthService>();
        if (auth != null && !await auth.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }
        // Ensure periodic sync is running even if user opened Usage stats without visiting Home first
        var usageService = GetUsageService();
        if (usageService != null)
            usageService.StartPeriodicSync();
        await LoadAsync();
    }

    private void OnViewChanged(object? sender, EventArgs e)
    {
        if (_ignorePickerChanges || ViewPicker.SelectedIndex < 0) return;
        _ = LoadAsync();
    }

    private void OnCategoryChanged(object? sender, EventArgs e)
    {
        if (_ignorePickerChanges || CategoryPicker.SelectedIndex < 0) return;
        _ = LoadAsync();
    }

    private void OnAppFilterChanged(object? sender, EventArgs e)
    {
        if (_ignorePickerChanges || AppPicker.SelectedIndex < 0) return;
        _ = LoadAsync();
    }

    private string GetGroupBy()
    {
        var i = ViewPicker.SelectedIndex;
        if (i == 1) return "week";
        if (i == 2) return "month";
        return "day";
    }

    private string? GetCategoryFilter()
    {
        if (CategoryPicker.SelectedIndex <= 0) return null;
        var s = CategoryPicker.SelectedItem?.ToString();
        return string.IsNullOrEmpty(s) || s == "All" ? null : s;
    }

    private string? GetAppFilter()
    {
        if (AppPicker.SelectedIndex <= 0) return null;
        var s = AppPicker.SelectedItem?.ToString();
        return string.IsNullOrEmpty(s) || s == "All" ? null : s;
    }

    private bool IsViewByCategory => ViewPicker.SelectedIndex == 3;
    private bool IsViewByApp => ViewPicker.SelectedIndex == 4;

    private async Task LoadAsync()
    {
        IsRefreshing = true;
        try
        {
            var usageService = GetUsageService();
            if (usageService == null)
            {
                EmptyView = "Service not available.";
                GroupedEntries.Clear();
                UpdateCharts(new List<UsageListEntry>());
                return;
            }
            // Push any pending usage so the list is up-to-date
            await usageService.SyncAsync();
            var from = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");
            var to = DateTime.Now.ToString("yyyy-MM-dd");
            var groupBy = GetGroupBy();
            var categoryFilter = GetCategoryFilter();
            var appFilter = GetAppFilter();
            var response = await usageService.GetUsageAsync(from, to, groupBy, categoryFilter, appFilter);
            if (response == null)
            {
                EmptyView = "Not signed in or couldn't load usage.";
                GroupedEntries.Clear();
                UpdateCharts(new List<UsageListEntry>());
                return;
            }
            var entries = response.Entries ?? new List<UsageListEntry>();

            // Merge distinct categories and apps for filter dropdowns
            var categories = entries.Select(e => e.Category ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToList();
            var apps = entries.Select(e => e.AppName ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToList();
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

            IEnumerable<UsageDateGroup> groups;
            if (IsViewByCategory)
            {
                var items = entries.Select(e => new UsageStatsItem { AppName = e.AppName ?? "", Category = e.Category ?? "", TotalSeconds = e.TotalSeconds }).ToList();
                groups = items.GroupBy(x => x.Category).OrderByDescending(g => g.Sum(i => i.TotalSeconds)).ThenBy(g => g.Key)
                    .Select(g => new UsageDateGroup(g.Key, g));
            }
            else if (IsViewByApp)
            {
                var items = entries.Select(e => new UsageStatsItem { AppName = e.AppName ?? "", Category = e.Category ?? "", TotalSeconds = e.TotalSeconds }).ToList();
                groups = items.GroupBy(x => x.AppName).OrderByDescending(g => g.Sum(i => i.TotalSeconds)).ThenBy(g => g.Key)
                    .Select(g => new UsageDateGroup(g.Key, g));
            }
            else
            {
                groups = entries
                    .GroupBy(e => e.Date)
                    .OrderByDescending(g => g.Key)
                    .Select(g => new UsageDateGroup(g.Key, g.Select(e => new UsageStatsItem
                    {
                        AppName = e.AppName ?? "",
                        Category = e.Category ?? "",
                        TotalSeconds = e.TotalSeconds
                    })));
            }

            GroupedEntries = new ObservableCollection<UsageDateGroup>(groups.ToList());
            EmptyView = GroupedEntries.Count == 0
                ? "No synced usage yet. Stay on Home for 15–30 seconds so usage can sync, then pull to refresh."
                : "Pull to refresh.";
            UpdateCharts(entries);
            UpdateInsightsAndRecommendation(entries);
        }
        catch (Exception ex)
        {
            EmptyView = "Error loading usage. Check backend is running.";
            GroupedEntries.Clear();
            UpdateCharts(new List<UsageListEntry>());
            System.Diagnostics.Debug.WriteLine($"UsageStats LoadAsync: {ex}");
        }
        finally
        {
            IsRefreshing = false;
        }
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
        double categoryTotal = byCategory.Sum(s => s.Value);
        _pieDrawable.Segments = byCategory;
        _pieDrawable.Total = categoryTotal;
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

    private static readonly string[] FocusCategories = { "Coding", "Productivity" };
    private static readonly string[] DistractionCategories = { "Gaming", "Chilling" };

    private static bool IsFocusCategory(string? category) =>
        FocusCategories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
    private static bool IsDistractionCategory(string? category) =>
        DistractionCategories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));

    private void UpdateInsightsAndRecommendation(List<UsageListEntry> entries)
    {
        if (entries.Count == 0)
        {
            InsightBiggestDistraction = "";
            InsightFocusThisWeek = "";
            InsightTrend = "";
            RecommendationChipText = "";
            return;
        }
        var now = DateTime.Now;
        var thisWeekStart = now.AddDays(-(int)now.DayOfWeek);
        var lastWeekStart = thisWeekStart.AddDays(-7);
        var thisWeekEntries = entries.Where(e => e.Date != null && string.CompareOrdinal(e.Date, thisWeekStart.ToString("yyyy-MM-dd")) >= 0 && string.CompareOrdinal(e.Date, now.ToString("yyyy-MM-dd")) <= 0).ToList();
        var lastWeekEntries = entries.Where(e => e.Date != null && string.CompareOrdinal(e.Date, lastWeekStart.ToString("yyyy-MM-dd")) >= 0 && string.CompareOrdinal(e.Date, thisWeekStart.AddDays(-1).ToString("yyyy-MM-dd")) <= 0).ToList();

        var distractionByCat = entries
            .Where(e => IsDistractionCategory(e.Category))
            .GroupBy(e => e.Category ?? "")
            .Select(g => new { Category = g.Key, Seconds = g.Sum(x => x.TotalSeconds) })
            .OrderByDescending(x => x.Seconds)
            .FirstOrDefault();
        InsightBiggestDistraction = distractionByCat != null && distractionByCat.Seconds > 0
            ? $"Biggest distraction: {distractionByCat.Category} ({(int)(distractionByCat.Seconds / 60)} min)"
            : "";

        var focusThisWeek = thisWeekEntries.Where(e => IsFocusCategory(e.Category)).Sum(e => e.TotalSeconds);
        InsightFocusThisWeek = $"Focus time this week: {(int)(focusThisWeek / 60)} min";

        var focusLastWeek = lastWeekEntries.Where(e => IsFocusCategory(e.Category)).Sum(e => e.TotalSeconds);
        if (focusLastWeek > 0)
        {
            var pct = (focusThisWeek - focusLastWeek) / focusLastWeek;
            InsightTrend = pct > 0.05 ? "Trend: Up vs last week" : pct < -0.05 ? "Trend: Down vs last week" : "Trend: Same vs last week";
        }
        else
            InsightTrend = focusThisWeek > 0 ? "Trend: Up vs last week" : "";

        RecommendationChipText = !string.IsNullOrEmpty(InsightBiggestDistraction) && distractionByCat != null
            ? $"Block {distractionByCat.Category} for 25 min"
            : "Start a 25 min focus session";
    }

    public ICommand RecommendationChipCommand => new Command(async () => await Shell.Current.GoToAsync(nameof(SessionPage)));

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
