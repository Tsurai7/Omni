using System.Linq;

namespace Omni.Client;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
        Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));
        Routing.RegisterRoute(nameof(UsageStatsPage), typeof(UsageStatsPage));
        Routing.RegisterRoute(nameof(SessionPage), typeof(SessionPage));
        Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));
        WireShellContentToServiceProvider();
    }

    /// <summary>Ensure Shell pages are created via DI so they get IActiveWindowTracker, IUsageService, etc.</summary>
    private void WireShellContentToServiceProvider()
    {
        if (MauiProgram.AppServices == null) return;
        foreach (var item in Items.OfType<FlyoutItem>())
        {
            foreach (var content in item.Items.OfType<ShellContent>())
            {
                var route = content.Route;
                content.ContentTemplate = route switch
                {
                    "MainPage" => new DataTemplate(() => MauiProgram.AppServices.GetService<MainPage>()!),
                    "UsageStatsPage" => new DataTemplate(() => MauiProgram.AppServices.GetService<UsageStatsPage>()!),
                    "SessionPage" => new DataTemplate(() => MauiProgram.AppServices.GetService<SessionPage>()!),
                    "TasksPage" => new DataTemplate(() => MauiProgram.AppServices.GetService<TasksPage>()!),
                    "AccountPage" => new DataTemplate(() => MauiProgram.AppServices.GetService<AccountPage>()!),
                    _ => content.ContentTemplate
                };
            }
        }
    }
}