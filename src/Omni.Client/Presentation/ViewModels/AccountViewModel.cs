using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Omni.Client.Abstractions;
using Omni.Client.Services;

namespace Omni.Client.Presentation.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly CalendarService _calendarService;
    private readonly IUsageService? _usageService;

    [ObservableProperty]
    private string _email = "-";

    [ObservableProperty]
    private string _avatarInitial = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isGoogleConnected;

    [ObservableProperty]
    private string _gCalStatusText = "Not connected";

    [ObservableProperty]
    private string _gCalConnectButtonText = "Connect";

    [ObservableProperty]
    private bool _gCalSyncRowVisible;

    [ObservableProperty]
    private string _gCalLastSyncText = "";

    [ObservableProperty]
    private int _dailyGoalMinutes = ProductivityPreferences.DefaultDailyGoalMinutes;

    [ObservableProperty]
    private string _notificationIntensity = ProductivityPreferences.DefaultNotificationIntensity;

    [ObservableProperty]
    private bool _streakVisible = ProductivityPreferences.DefaultStreakVisible;

    [ObservableProperty]
    private string _focusHoursStart = ProductivityPreferences.DefaultFocusHoursStart;

    [ObservableProperty]
    private string _focusHoursEnd = ProductivityPreferences.DefaultFocusHoursEnd;

    public static readonly string[] NotificationIntensityOptions = { "Low", "Medium", "High" };
    public static readonly int[] DailyGoalOptions = { 30, 60, 90, 120 };

    public AccountViewModel(IAuthService authService, CalendarService calendarService, IUsageService? usageService = null)
    {
        _authService = authService;
        _calendarService = calendarService;
        _usageService = usageService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;

        if (!await _authService.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }

        var user = await _authService.GetCurrentUserAsync();
        Email = user?.Email ?? "-";
        if (!string.IsNullOrEmpty(user?.Email))
            AvatarInitial = user.Email[0].ToString().ToUpperInvariant();

        LoadProductivityPreferences();
        await RefreshGCalStatusAsync();

        IsLoading = false;
    }

    private void LoadProductivityPreferences()
    {
        DailyGoalMinutes = ProductivityPreferences.GetDailyGoalMinutes();
        NotificationIntensity = ProductivityPreferences.GetNotificationIntensity();
        StreakVisible = ProductivityPreferences.GetStreakVisible();
        FocusHoursStart = ProductivityPreferences.GetFocusHoursStart();
        FocusHoursEnd = ProductivityPreferences.GetFocusHoursEnd();
    }

    public async Task RefreshGCalStatusAsync()
    {
        try
        {
            var status = await _calendarService.RefreshStatusAsync();
            if (status == null || !status.Connected)
            {
                IsGoogleConnected = false;
                GCalStatusText = "Not connected";
                GCalConnectButtonText = "Connect";
                GCalSyncRowVisible = false;
            }
            else
            {
                IsGoogleConnected = true;
                var emailPart = string.IsNullOrEmpty(status.Email) ? "" : $" ({status.Email})";
                GCalStatusText = $"Connected{emailPart}";
                GCalConnectButtonText = "Disconnect";
                GCalSyncRowVisible = true;
                GCalLastSyncText = status.LastSyncedAt != null
                    ? $"Last synced {status.LastSyncedAt}"
                    : "Never synced";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AccountViewModel.RefreshGCalStatusAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task SignOutAsync()
    {
        _usageService?.StopPeriodicSync();
        _authService.Logout();
        await Shell.Current.GoToAsync(nameof(LoginPage));
    }

    public void SetDailyGoal(int minutes)
    {
        DailyGoalMinutes = minutes;
        ProductivityPreferences.SetDailyGoalMinutes(minutes);
    }

    public void SetNotificationIntensity(string intensity)
    {
        NotificationIntensity = intensity;
        ProductivityPreferences.SetNotificationIntensity(intensity);
    }

    public void SetStreakVisible(bool visible)
    {
        StreakVisible = visible;
        ProductivityPreferences.SetStreakVisible(visible);
    }
}
