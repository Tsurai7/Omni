namespace Omni.Client;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
        Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));
        Routing.RegisterRoute(nameof(UsageStatsPage), typeof(UsageStatsPage));
        Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));
    }
}