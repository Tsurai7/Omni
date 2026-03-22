using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Omni.Client.Abstractions;
using Omni.Client.Models.Usage;
using Omni.Client.Models.Session;

namespace Omni.Client.Presentation.ViewModels;

public partial class UsageStatsViewModel : ObservableObject
{
    private readonly IUsageService _usageService;
    private readonly ISessionService _sessionService;
    private readonly IProductivityService _productivityService;
    private DateTime _lastLoaded = DateTime.MinValue;

    private record CachedPeriodData(UsageListResponse? Usage, SessionListResponse? Sessions, DateTime LoadedAt);
    private readonly Dictionary<string, CachedPeriodData> _periodCache = new();

    public bool IsDataStale(TimeSpan threshold) => DateTime.UtcNow - _lastLoaded > threshold;

    [ObservableProperty]
    private string _selectedPeriod = "Today";

    [ObservableProperty]
    private string? _selectedCategory;

    [ObservableProperty]
    private string? _selectedApp;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private UsageListResponse? _usageData;

    [ObservableProperty]
    private SessionListResponse? _sessionData;

    [ObservableProperty]
    private string _insightText = "";

    [ObservableProperty]
    private string _totalFocusTime = "—";

    [ObservableProperty]
    private string _topCategory = "";

    [ObservableProperty]
    private string _sessionCount = "0";

    [ObservableProperty]
    private string _summaryAvgScore = "—";

    [ObservableProperty]
    private string _summaryPeakHour = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInsights))]
    private string _insightBiggestDistraction = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInsights))]
    private string _insightFocusThisWeek = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInsights))]
    private string _insightTrend = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecommendation))]
    private string _recommendationChipText = "";

    [ObservableProperty]
    private ObservableCollection<UsageDateGroup> _groupedEntries = new();

    [ObservableProperty]
    private string _emptyView = "Pull to load synced usage.";

    public bool HasInsights =>
        !string.IsNullOrEmpty(InsightBiggestDistraction) ||
        !string.IsNullOrEmpty(InsightFocusThisWeek) ||
        !string.IsNullOrEmpty(InsightTrend);

    public bool HasRecommendation => !string.IsNullOrEmpty(RecommendationChipText);

    public event Action? DataLoaded;
    public event Action<string>? LoadFailed;

    private string? _recommendationNotificationId;

    public UsageStatsViewModel(
        IUsageService usageService,
        ISessionService sessionService,
        IProductivityService productivityService)
    {
        _usageService = usageService;
        _sessionService = sessionService;
        _productivityService = productivityService;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            _usageService.StartPeriodicSync();
            try { await _usageService.SyncAsync(ct); } catch { /* best-effort */ }

            var (from, to) = GetDateRange();
            var t1 = _usageService.GetUsageAsync(from, to, "day", SelectedCategory, SelectedApp, ct);
            var t2 = _sessionService.GetSessionsAsync(from, to, ct);

            await Task.WhenAll(t1, t2);

            UsageData = await t1;
            SessionData = await t2;

            _periodCache[SelectedPeriod] = new CachedPeriodData(UsageData, SessionData, DateTime.UtcNow);

            await ComputeSummaryAsync(ct);
            _lastLoaded = DateTime.UtcNow;
            DataLoaded?.Invoke();

            _ = PreloadOtherPeriodsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UsageStatsViewModel.LoadAsync: {ex.Message}");
            LoadFailed?.Invoke(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ComputeSummaryAsync(CancellationToken ct)
    {
        var entries = UsageData?.Entries ?? new List<UsageListEntry>();

        if (entries.Count == 0)
        {
            TotalFocusTime = "0m";
            TopCategory = "";
            SummaryPeakHour = "—";
            SummaryAvgScore = "—";
            InsightText = "No usage data for this period.";
            InsightBiggestDistraction = "";
            InsightFocusThisWeek = "";
            InsightTrend = "";
            RecommendationChipText = "";
            GroupedEntries = new ObservableCollection<UsageDateGroup>();
            EmptyView = "No synced usage yet. Stay on Home for 15–30 seconds so usage can sync, then pull to refresh.";
        }
        else
        {
            var focusSeconds = entries
                .Where(e => IsFocusCategory(e.Category))
                .Sum(e => e.TotalSeconds);
            var focusMinutes = (int)(focusSeconds / 60);
            TotalFocusTime = focusMinutes >= 60
                ? $"{focusMinutes / 60}h {focusMinutes % 60}m"
                : $"{focusMinutes}m";

            TopCategory = entries
                .GroupBy(e => e.Category ?? "Other")
                .OrderByDescending(g => g.Sum(e => e.TotalSeconds))
                .FirstOrDefault()?.Key ?? "";

            SummaryPeakHour = ComputePeakHour(entries);
            InsightText = $"You spent {TotalFocusTime} in focused work.";

            var groups = entries
                .GroupBy(e => e.Date)
                .OrderByDescending(g => g.Key)
                .Select(g => new UsageDateGroup(g.Key,
                    g.Select(e => new UsageStatsItem
                    {
                        AppName = e.AppName ?? "",
                        Category = e.Category ?? "",
                        TotalSeconds = e.TotalSeconds
                    })));
            GroupedEntries = new ObservableCollection<UsageDateGroup>(groups.ToList());
            EmptyView = "Pull to refresh.";

            await TryLoadServerInsightsAsync(entries, ct);
        }

        SessionCount = (SessionData?.Entries?.Count ?? 0).ToString();
    }

    private async Task TryLoadServerInsightsAsync(List<UsageListEntry> entries, CancellationToken ct)
    {
        try
        {
            var notifications = await _productivityService.GetNotificationsAsync(unreadOnly: false, cancellationToken: ct);
            var insights = notifications
                .Where(n => string.Equals(n.Type, "insight", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();
            var recommendation = notifications
                .FirstOrDefault(n => string.Equals(n.Type, "recommendation", StringComparison.OrdinalIgnoreCase));

            if (insights.Count > 0 || recommendation != null)
            {
                InsightBiggestDistraction = insights.ElementAtOrDefault(0)?.Title ?? "";
                InsightFocusThisWeek = insights.ElementAtOrDefault(1)?.Title ?? "";
                InsightTrend = insights.ElementAtOrDefault(2)?.Title ?? "";
                RecommendationChipText = recommendation?.Title ?? "";
                _recommendationNotificationId = recommendation?.Id;
                return;
            }
        }
        catch { }

        ComputeClientInsights(entries);
        _recommendationNotificationId = null;
    }

    private void ComputeClientInsights(List<UsageListEntry> entries)
    {
        var now = DateTime.Now;
        var thisWeekStart = now.AddDays(-(int)now.DayOfWeek);
        var lastWeekStart = thisWeekStart.AddDays(-7);

        var thisWeekEntries = entries.Where(e =>
            e.Date != null &&
            string.CompareOrdinal(e.Date, thisWeekStart.ToString("yyyy-MM-dd")) >= 0 &&
            string.CompareOrdinal(e.Date, now.ToString("yyyy-MM-dd")) <= 0).ToList();
        var lastWeekEntries = entries.Where(e =>
            e.Date != null &&
            string.CompareOrdinal(e.Date, lastWeekStart.ToString("yyyy-MM-dd")) >= 0 &&
            string.CompareOrdinal(e.Date, thisWeekStart.AddDays(-1).ToString("yyyy-MM-dd")) <= 0).ToList();

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
            var pct = (focusThisWeek - focusLastWeek) / (double)focusLastWeek;
            InsightTrend = pct > 0.05 ? "Trend: Up vs last week"
                : pct < -0.05 ? "Trend: Down vs last week"
                : "Trend: Same vs last week";
        }
        else
        {
            InsightTrend = focusThisWeek > 0 ? "Trend: Up vs last week" : "";
        }

        RecommendationChipText = !string.IsNullOrEmpty(InsightBiggestDistraction) && distractionByCat != null
            ? $"Block {distractionByCat.Category} for 25 min"
            : "Start a 25 min focus session";
    }

    [RelayCommand]
    public async Task RecommendationChipAsync()
    {
        if (!string.IsNullOrEmpty(_recommendationNotificationId))
        {
            try { await _productivityService.MarkAsReadAsync(_recommendationNotificationId); } catch { }
        }
        await Shell.Current.GoToAsync("//SessionPage");
    }

    [RelayCommand]
    public void SelectPeriod(string period)
    {
        SelectedPeriod = period;
    }

    /// <summary>
    /// Cache-first period switch. Uses preloaded data instantly when available,
    /// otherwise falls back to a full load.
    /// </summary>
    [RelayCommand]
    public async Task SwitchPeriodAsync(CancellationToken ct = default)
    {
        if (_periodCache.TryGetValue(SelectedPeriod, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < TimeSpan.FromMinutes(5) &&
            SelectedCategory == null && SelectedApp == null)
        {
            IsLoading = true;
            try
            {
                UsageData = cached.Usage;
                SessionData = cached.Sessions;
                await ComputeSummaryAsync(ct);
                _lastLoaded = DateTime.UtcNow;
                DataLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UsageStatsViewModel.SwitchPeriodAsync (cache): {ex.Message}");
                LoadFailed?.Invoke(ex.Message);
            }
            finally { IsLoading = false; }
            return;
        }

        await LoadAsync(ct);
    }

    private async Task PreloadOtherPeriodsAsync()
    {
        foreach (var period in new[] { "Today", "Week", "Month" })
        {
            if (period == SelectedPeriod || _periodCache.ContainsKey(period)) continue;
            try
            {
                var (from, to) = GetDateRangeForPeriod(period);
                var t1 = _usageService.GetUsageAsync(from, to, "day", null, null, default);
                var t2 = _sessionService.GetSessionsAsync(from, to, default);
                await Task.WhenAll(t1, t2);
                _periodCache[period] = new CachedPeriodData(await t1, await t2, DateTime.UtcNow);
            }
            catch { /* best-effort background preload */ }
        }
    }

    private static string ComputePeakHour(List<UsageListEntry> entries)
    {
        var peakGroup = entries
            .Where(e => IsFocusCategory(e.Category) && DateTime.TryParse(e.Date, out _))
            .GroupBy(e => DateTime.Parse(e.Date).Hour)
            .OrderByDescending(g => g.Sum(x => x.TotalSeconds))
            .FirstOrDefault();
        return peakGroup != null ? $"{peakGroup.Key:D2}:00" : "—";
    }

    private (string from, string to) GetDateRange() => GetDateRangeForPeriod(SelectedPeriod);

    private static (string from, string to) GetDateRangeForPeriod(string period)
    {
        var today = DateTime.Today;
        return period switch
        {
            "Week" => (today.AddDays(-6).ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd")),
            "Month" => (today.AddDays(-29).ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd")),
            _ => (today.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"))
        };
    }

    private static readonly string[] FocusCategories = { "Coding", "Productivity" };
    private static readonly string[] DistractionCategories = { "Gaming", "Chilling" };

    private static bool IsFocusCategory(string? c) =>
        FocusCategories.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase));

    private static bool IsDistractionCategory(string? c) =>
        DistractionCategories.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase));
}
