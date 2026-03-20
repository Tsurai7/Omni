using System.ComponentModel;
using System.Runtime.CompilerServices;
using Omni.Client.Abstractions;
using Omni.Client.Services;

namespace Omni.Client;

public partial class AccountPage : ContentPage, INotifyPropertyChanged
{
    private readonly IAuthService _authService;
    private readonly IUsageService? _usageService;
    private CalendarService? _calendarService;
    private int _dailyGoalMinutes = ProductivityPreferences.DefaultDailyGoalMinutes;
    private string _notificationIntensity = ProductivityPreferences.DefaultNotificationIntensity;
    private bool _streakVisible = ProductivityPreferences.DefaultStreakVisible;
    private string _focusHoursStart = ProductivityPreferences.DefaultFocusHoursStart;
    private string _focusHoursEnd = ProductivityPreferences.DefaultFocusHoursEnd;

    public static readonly string[] NotificationIntensityOptions = { "Low", "Medium", "High" };
    public static readonly int[] DailyGoalOptions = { 30, 60, 90, 120 };

    public AccountPage()
    {
        InitializeComponent();
        BindingContext = this;
        _authService = MauiProgram.AppServices?.GetService<IAuthService>()
            ?? throw new InvalidOperationException("IAuthService not registered.");
        _usageService = MauiProgram.AppServices?.GetService<IUsageService>();
        DailyGoalPicker.ItemsSource = DailyGoalOptions.Select(i => i.ToString()).ToList();
        NotificationIntensityPicker.ItemsSource = NotificationIntensityOptions;
    }

    private CalendarService GetCalendarService() =>
        _calendarService ??= MauiProgram.AppServices?.GetService<CalendarService>()!;

    public int DailyGoalMinutes
    {
        get => _dailyGoalMinutes;
        set { if (_dailyGoalMinutes != value) { _dailyGoalMinutes = value; OnPropertyChanged(); ProductivityPreferences.SetDailyGoalMinutes(value); } }
    }

    public string NotificationIntensity
    {
        get => _notificationIntensity;
        set { if (_notificationIntensity != value) { _notificationIntensity = value; OnPropertyChanged(); ProductivityPreferences.SetNotificationIntensity(value); } }
    }

    public bool StreakVisible
    {
        get => _streakVisible;
        set { if (_streakVisible != value) { _streakVisible = value; OnPropertyChanged(); ProductivityPreferences.SetStreakVisible(value); } }
    }

    public string FocusHoursStart
    {
        get => _focusHoursStart;
        set { if (_focusHoursStart != value) { _focusHoursStart = value; OnPropertyChanged(); ProductivityPreferences.SetFocusHoursStart(value); } }
    }

    public string FocusHoursEnd
    {
        get => _focusHoursEnd;
        set { if (_focusHoursEnd != value) { _focusHoursEnd = value; OnPropertyChanged(); ProductivityPreferences.SetFocusHoursEnd(value); } }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        LoadingIndicator.IsVisible = true;
        SignOutButton.IsEnabled = false;
        EmailLabel.Text = "-";

        if (!await _authService.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }

        var user = await _authService.GetCurrentUserAsync();
        LoadingIndicator.IsVisible = false;
        EmailLabel.IsVisible = true;
        SignOutButton.IsEnabled = true;
        EmailLabel.Text = user?.Email ?? "-";
        if (!string.IsNullOrEmpty(user?.Email))
            AvatarInitialsLabel.Text = user.Email[0].ToString().ToUpperInvariant();
        LoadProductivityPreferences();
        await RefreshGCalStatusAsync();
    }

    private void LoadProductivityPreferences()
    {
        _dailyGoalMinutes = ProductivityPreferences.GetDailyGoalMinutes();
        _notificationIntensity = ProductivityPreferences.GetNotificationIntensity();
        _streakVisible = ProductivityPreferences.GetStreakVisible();
        _focusHoursStart = ProductivityPreferences.GetFocusHoursStart();
        _focusHoursEnd = ProductivityPreferences.GetFocusHoursEnd();
        var goalIndex = Array.IndexOf(DailyGoalOptions, _dailyGoalMinutes);
        if (goalIndex < 0) goalIndex = 1;
        DailyGoalPicker.SelectedIndex = goalIndex;
        var intensityIndex = NotificationIntensityOptions.ToList().IndexOf(_notificationIntensity);
        if (intensityIndex < 0) intensityIndex = 1;
        NotificationIntensityPicker.SelectedIndex = intensityIndex;
        StreakVisibleSwitch.IsToggled = _streakVisible;
        OnPropertyChanged(nameof(DailyGoalMinutes));
        OnPropertyChanged(nameof(NotificationIntensity));
        OnPropertyChanged(nameof(StreakVisible));
        OnPropertyChanged(nameof(FocusHoursStart));
        OnPropertyChanged(nameof(FocusHoursEnd));
    }

    private void OnDailyGoalChanged(object? sender, EventArgs e)
    {
        if (DailyGoalPicker.SelectedIndex >= 0 && DailyGoalPicker.SelectedIndex < DailyGoalOptions.Length)
            DailyGoalMinutes = DailyGoalOptions[DailyGoalPicker.SelectedIndex];
    }

    private void OnNotificationIntensityChanged(object? sender, EventArgs e)
    {
        if (NotificationIntensityPicker.SelectedIndex >= 0 && NotificationIntensityPicker.SelectedIndex < NotificationIntensityOptions.Length)
            NotificationIntensity = NotificationIntensityOptions[NotificationIntensityPicker.SelectedIndex];
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        _usageService?.StopPeriodicSync();
        _authService.Logout();
        await Shell.Current.GoToAsync(nameof(LoginPage));
    }

    // ── Google Calendar ───────────────────────────────────────────────────

    private async Task RefreshGCalStatusAsync()
    {
        try
        {
            var status = await GetCalendarService().RefreshStatusAsync();
            if (status == null || !status.Connected)
            {
                GCalStatusLabel.Text = "Not connected";
                GCalConnectButton.Text = "Connect";
                GCalConnectButton.Style = (Style)Resources["ProductivityPillButton"];
                GCalSyncRow.IsVisible = false;
                GCalSyncDivider.IsVisible = false;
            }
            else
            {
                var email = string.IsNullOrEmpty(status.Email) ? "" : $" ({status.Email})";
                GCalStatusLabel.Text = $"Connected{email}";
                GCalConnectButton.Text = "Disconnect";
                GCalConnectButton.Style = (Style)Resources["ProductivityDangerButton"];
                GCalSyncRow.IsVisible = true;
                GCalSyncDivider.IsVisible = true;
                GCalLastSyncLabel.Text = status.LastSyncedAt != null
                    ? $"Last synced {status.LastSyncedAt}"
                    : "Never synced";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AccountPage.RefreshGCalStatusAsync: {ex.Message}");
        }
    }

    private async void OnGCalConnectClicked(object? sender, EventArgs e)
    {
        var svc = GetCalendarService();

        if (svc.IsConnected)
        {
            var confirm = await DisplayAlertAsync("Disconnect Google Calendar",
                "This will remove calendar sync. Your tasks will remain intact.", "Disconnect", "Cancel");
            if (!confirm) return;
            await svc.DisconnectAsync();
            await RefreshGCalStatusAsync();
            return;
        }

        // Open Google sign-in in the system browser; backend handles the callback
        GCalConnectButton.IsEnabled = false;
        GCalStatusLabel.Text = "Opening Google sign-in…";

        try
        {
            var authUrl = await svc.GetAuthUrlAsync();
            if (string.IsNullOrEmpty(authUrl))
            {
                await DisplayAlertAsync("Error", "Could not get Google sign-in URL. Check backend configuration.", "OK");
                await RefreshGCalStatusAsync();
                GCalConnectButton.IsEnabled = true;
                return;
            }

            await Browser.Default.OpenAsync(authUrl, BrowserLaunchMode.SystemPreferred);

            // Ask user to confirm they've completed sign-in
            await DisplayAlertAsync("Google Calendar",
                "Complete sign-in in the browser, then tap OK to refresh status.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AccountPage.OnGCalConnectClicked: {ex.Message}");
            await DisplayAlertAsync("Error", "Could not open Google sign-in. Please try again.", "OK");
        }

        GCalConnectButton.IsEnabled = true;
        await RefreshGCalStatusAsync();
    }

    private async void OnGCalSyncClicked(object? sender, EventArgs e)
    {
        GCalSyncButton.IsEnabled = false;
        GCalLastSyncLabel.Text = "Syncing…";
        var success = await GetCalendarService().SyncAsync();
        GCalSyncButton.IsEnabled = true;
        GCalLastSyncLabel.Text = success ? $"Synced {DateTime.Now:HH:mm}" : "Sync failed";
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
