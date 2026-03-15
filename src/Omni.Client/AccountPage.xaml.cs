using System.ComponentModel;
using System.Runtime.CompilerServices;
using Omni.Client.Abstractions;

namespace Omni.Client;

public partial class AccountPage : ContentPage, INotifyPropertyChanged
{
    private readonly IAuthService _authService;
    private readonly IUsageService? _usageService;
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
        SignOutButton.IsEnabled = true;
        EmailLabel.Text = user?.Email ?? "-";
        LoadProductivityPreferences();
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

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
