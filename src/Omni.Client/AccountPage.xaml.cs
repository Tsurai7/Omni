using Omni.Client.Abstractions;

namespace Omni.Client;

public partial class AccountPage : ContentPage
{
    private readonly IAuthService _authService;
    private readonly IUsageService? _usageService;

    public AccountPage()
    {
        InitializeComponent();
        _authService = MauiProgram.AppServices?.GetService<IAuthService>()
            ?? throw new InvalidOperationException("IAuthService not registered.");
        _usageService = MauiProgram.AppServices?.GetService<IUsageService>();
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
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        _usageService?.StopPeriodicSync();
        _authService.Logout();
        await Shell.Current.GoToAsync(nameof(LoginPage));
    }
}
