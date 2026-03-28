using Omni.Client.Presentation.ViewModels;
using Omni.Client.Services;

namespace Omni.Client;

public partial class AccountPage : ContentPage
{
    private readonly AccountViewModel _vm;
    private readonly CalendarService _calendarService;
    private readonly IThemeService _themeService;

    public static readonly string[] NotificationIntensityOptions = AccountViewModel.NotificationIntensityOptions;
    public static readonly int[] DailyGoalOptions = AccountViewModel.DailyGoalOptions;

    public AccountPage(AccountViewModel vm, CalendarService calendarService, IThemeService themeService)
    {
        InitializeComponent();
        _vm = vm;
        _calendarService = calendarService;
        _themeService = themeService;
        BindingContext = vm;

        DailyGoalPicker.ItemsSource = DailyGoalOptions.Select(i => i.ToString()).ToList();
        NotificationIntensityPicker.ItemsSource = NotificationIntensityOptions;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AccountViewModel.IsGoogleConnected)
                               or nameof(AccountViewModel.GCalStatusText)
                               or nameof(AccountViewModel.GCalConnectButtonText)
                               or nameof(AccountViewModel.GCalLastSyncText)
                               or nameof(AccountViewModel.GCalSyncRowVisible))
                MainThread.BeginInvokeOnMainThread(UpdateGCalUI);
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        var goalIndex = Array.IndexOf(DailyGoalOptions, _vm.DailyGoalMinutes);
        DailyGoalPicker.SelectedIndex = goalIndex < 0 ? 1 : goalIndex;

        var intensityIndex = NotificationIntensityOptions.ToList().IndexOf(_vm.NotificationIntensity);
        NotificationIntensityPicker.SelectedIndex = intensityIndex < 0 ? 1 : intensityIndex;

        StreakVisibleSwitch.IsToggled = _vm.StreakVisible;
        UpdateGCalUI();
        UpdateThemeButtons(_themeService.Current);

        if (_vm.IsDataStale(TimeSpan.FromSeconds(60)))
            _ = _vm.LoadAsync();
    }

    // ── Theme switching ───────────────────────────────────────────────────────

    private void OnThemeDarkClicked(object? sender, EventArgs e)   => SetTheme(AppColorTheme.Dark);
    private void OnThemeSystemClicked(object? sender, EventArgs e) => SetTheme(AppColorTheme.System);
    private void OnThemeLightClicked(object? sender, EventArgs e)  => SetTheme(AppColorTheme.Light);

    private void SetTheme(AppColorTheme theme)
    {
        _themeService.Apply(theme);
        UpdateThemeButtons(theme);
    }

    private void UpdateThemeButtons(AppColorTheme active)
    {
        var pill    = (Style)Application.Current!.Resources["ProductivityPillButton"];
        var segment = (Style)Application.Current!.Resources["ProductivitySegmentButton"];

        ThemeDarkBtn.Style   = active == AppColorTheme.Dark   ? pill : segment;
        ThemeSystemBtn.Style = active == AppColorTheme.System ? pill : segment;
        ThemeLightBtn.Style  = active == AppColorTheme.Light  ? pill : segment;
    }

    // ── Google Calendar ───────────────────────────────────────────────────────

    private void UpdateGCalUI()
    {
        GCalStatusLabel.Text = _vm.GCalStatusText;
        GCalConnectButton.Text = _vm.GCalConnectButtonText;
        if (_vm.IsGoogleConnected)
        {
            GCalConnectButton.Style = (Style)Application.Current!.Resources["ProductivityDangerButton"];
            GCalSyncRow.IsVisible = true;
            GCalSyncDivider.IsVisible = true;
            GCalLastSyncLabel.Text = _vm.GCalLastSyncText;
        }
        else
        {
            GCalConnectButton.Style = (Style)Application.Current!.Resources["ProductivityPillButton"];
            GCalSyncRow.IsVisible = false;
            GCalSyncDivider.IsVisible = false;
        }
    }

    private void OnDailyGoalChanged(object? sender, EventArgs e)
    {
        if (DailyGoalPicker.SelectedIndex >= 0 && DailyGoalPicker.SelectedIndex < DailyGoalOptions.Length)
            _vm.SetDailyGoal(DailyGoalOptions[DailyGoalPicker.SelectedIndex]);
    }

    private void OnNotificationIntensityChanged(object? sender, EventArgs e)
    {
        if (NotificationIntensityPicker.SelectedIndex >= 0 && NotificationIntensityPicker.SelectedIndex < NotificationIntensityOptions.Length)
            _vm.SetNotificationIntensity(NotificationIntensityOptions[NotificationIntensityPicker.SelectedIndex]);
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
        => await _vm.SignOutAsync();

    private async void OnGCalConnectClicked(object? sender, EventArgs e)
    {
        if (_vm.IsGoogleConnected)
        {
            var confirm = await DisplayAlertAsync("Disconnect Google Calendar",
                "This will remove calendar sync. Your tasks will remain intact.", "Disconnect", "Cancel");
            if (!confirm) return;
            await _calendarService.DisconnectAsync();
            await _vm.RefreshGCalStatusAsync();
            UpdateGCalUI();
            return;
        }

        GCalConnectButton.IsEnabled = false;
        GCalStatusLabel.Text = "Opening Google sign-in…";

        try
        {
            var authUrl = await _calendarService.GetAuthUrlAsync();
            if (string.IsNullOrEmpty(authUrl))
            {
                await DisplayAlertAsync("Error", "Could not get Google sign-in URL. Check backend configuration.", "OK");
                await _vm.RefreshGCalStatusAsync();
                UpdateGCalUI();
                GCalConnectButton.IsEnabled = true;
                return;
            }

            await Browser.Default.OpenAsync(authUrl, BrowserLaunchMode.SystemPreferred);
            await DisplayAlertAsync("Google Calendar",
                "Complete sign-in in the browser, then tap OK to refresh status.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AccountPage.OnGCalConnectClicked: {ex.Message}");
            await DisplayAlertAsync("Error", "Could not open Google sign-in. Please try again.", "OK");
        }

        GCalConnectButton.IsEnabled = true;
        await _vm.RefreshGCalStatusAsync();
        UpdateGCalUI();
    }

    private async void OnGCalSyncClicked(object? sender, EventArgs e)
    {
        GCalSyncButton.IsEnabled = false;
        GCalLastSyncLabel.Text = "Syncing…";
        var success = await _calendarService.SyncAsync();
        GCalSyncButton.IsEnabled = true;
        GCalLastSyncLabel.Text = success ? $"Synced {DateTime.Now:HH:mm}" : "Sync failed";
    }
}
