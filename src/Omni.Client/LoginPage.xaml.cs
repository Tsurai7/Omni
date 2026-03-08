using Omni.Client.Abstractions;

namespace Omni.Client;

public partial class LoginPage : ContentPage
{
    private readonly IAuthService _authService;

    public LoginPage()
    {
        InitializeComponent();
        _authService = MauiProgram.AppServices?.GetService<IAuthService>()
            ?? throw new InvalidOperationException("IAuthService not registered.");
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";

        ErrorLabel.IsVisible = false;
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ErrorLabel.Text = "Please enter email and password.";
            ErrorLabel.IsVisible = true;
            return;
        }

        LoginButton.IsEnabled = false;
        try
        {
            var result = await _authService.LoginAsync(email, password);
            if (result != null)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }
            ErrorLabel.Text = "Invalid email or password.";
            ErrorLabel.IsVisible = true;
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = "Connection error. Check the server.";
            ErrorLabel.IsVisible = true;
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private async void OnGoToRegister(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(RegisterPage));
    }
}
