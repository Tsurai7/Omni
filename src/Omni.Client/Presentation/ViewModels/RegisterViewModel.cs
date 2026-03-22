using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Omni.Client.Abstractions;

namespace Omni.Client.Presentation.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly IAuthService _authService;

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _confirmPassword = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _errorVisible;

    [ObservableProperty]
    private bool _isBusy;

    public RegisterViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        ErrorVisible = false;
        var email = Email.Trim();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Please enter email and password.";
            ErrorVisible = true;
            return;
        }
        if (Password.Length < 8)
        {
            ErrorMessage = "Password must be at least 8 characters.";
            ErrorVisible = true;
            return;
        }
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            ErrorVisible = true;
            return;
        }

        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _authService.RegisterAsync(email, Password, cts.Token);
            if (result != null)
            {
                await Shell.Current.GoToAsync("../..");
                return;
            }
            var serverError = _authService.LastAuthError?.ToLowerInvariant() ?? "";
            ErrorMessage = serverError.Contains("already") || serverError.Contains("registered")
                ? "An account with this email already exists. Try logging in instead."
                : string.IsNullOrEmpty(serverError)
                    ? "Registration failed. Please try again."
                    : $"Registration failed: {_authService.LastAuthError}";
            ErrorVisible = true;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Request timed out. Check the server and try again.";
            ErrorVisible = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = "Connection error. Check the server.";
            ErrorVisible = true;
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoToLoginAsync()
        => await Shell.Current.GoToAsync("..");
}
