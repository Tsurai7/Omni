using Omni.Client.Presentation.ViewModels;

namespace Omni.Client;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
