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
        try
        {
            var result = await _authService.RegisterAsync(email, password);
            if (result != null)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }
            ErrorLabel.Text = "Registration failed. Email may already be in use.";
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
        }
    }

    private async void OnGoToLogin(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
