using Omni.Client.Abstractions;

namespace Omni.Client;

public partial class RegisterPage : ContentPage
{
    private readonly IAuthService _authService;

    public RegisterPage()
    {
        InitializeComponent();
        _authService = MauiProgram.AppServices?.GetService<IAuthService>()
            ?? throw new InvalidOperationException("IAuthService not registered.");
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";
        var confirm = ConfirmEntry.Text ?? "";

        ErrorLabel.IsVisible = false;
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ErrorLabel.Text = "Please enter email and password.";
            ErrorLabel.IsVisible = true;
            return;
        }
        if (password.Length < 8)
        {
            ErrorLabel.Text = "Password must be at least 8 characters.";
            ErrorLabel.IsVisible = true;
            return;
        }
        if (password != confirm)
        {
            ErrorLabel.Text = "Passwords do not match.";
            ErrorLabel.IsVisible = true;
            return;
        }

        RegisterButton.IsEnabled = false;
        RegisterIndicator.IsRunning = true;
        RegisterIndicator.IsVisible = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _authService.RegisterAsync(email, password, cts.Token);
            if (result != null)
            {
                await Shell.Current.GoToAsync("../..");
                return;
            }
            var serverError = _authService.LastAuthError?.ToLowerInvariant() ?? "";
            ErrorLabel.Text = serverError.Contains("already") || serverError.Contains("registered")
                ? "An account with this email already exists. Try logging in instead."
                : string.IsNullOrEmpty(serverError)
                    ? "Registration failed. Please try again."
                    : $"Registration failed: {_authService.LastAuthError}";
            ErrorLabel.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            ErrorLabel.Text = "Request timed out. Check the server and try again.";
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
            RegisterButton.IsEnabled = true;
            RegisterIndicator.IsRunning = false;
            RegisterIndicator.IsVisible = false;
        }
    }

    private async void OnGoToLogin(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
