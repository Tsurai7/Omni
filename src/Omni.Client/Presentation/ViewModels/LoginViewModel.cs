using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Omni.Client.Abstractions;

namespace Omni.Client.Presentation.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _errorVisible;

    [ObservableProperty]
    private bool _isBusy;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorVisible = false;
        var email = Email.Trim();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Please enter email and password.";
            ErrorVisible = true;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authService.LoginAsync(email, Password);
            if (result != null)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }
            ErrorMessage = string.IsNullOrEmpty(_authService.LastAuthError)
                ? "Invalid email or password."
                : _authService.LastAuthError;
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
    private async Task GoToRegisterAsync()
        => await Shell.Current.GoToAsync(nameof(RegisterPage));
}
