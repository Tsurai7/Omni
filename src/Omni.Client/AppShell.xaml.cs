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
        Routing.RegisterRoute(nameof(DigestPage), typeof(DigestPage));
        WireShellContentToServiceProvider();
    }

    /// <summary>Ensure Shell pages are created via DI so they get IActiveWindowTracker, IUsageService, etc.</summary>
    private void WireShellContentToServiceProvider()
    {
        if (MauiProgram.AppServices == null) return;

        // Walk TabBar → Tab → ShellContent
        foreach (var tabBar in Items.OfType<TabBar>())
        {
            foreach (var tab in tabBar.Items.OfType<Tab>())
            {
                foreach (var content in tab.Items.OfType<ShellContent>())
                {
                    SetContentTemplate(content);
                }
            }
        }

        // Fallback: also walk FlyoutItems for forward-compat
        foreach (var item in Items.OfType<FlyoutItem>())
        {
            foreach (var section in item.Items)
            {
                foreach (var content in section.Items.OfType<ShellContent>())
                {
                    SetContentTemplate(content);
                }
            }
        }
    }

    private void SetContentTemplate(ShellContent content)
    {
        if (MauiProgram.AppServices == null) return;
        var route = content.Route;
        content.ContentTemplate = route switch
        {
            "MainPage"       => new DataTemplate(() => MauiProgram.AppServices.GetService<MainPage>()!),
            "UsageStatsPage" => new DataTemplate(() => MauiProgram.AppServices.GetService<UsageStatsPage>()!),
            "SessionPage"    => new DataTemplate(() => MauiProgram.AppServices.GetService<SessionPage>()!),
            "TasksPage"      => new DataTemplate(() => MauiProgram.AppServices.GetService<TasksPage>()!),
            "AccountPage"    => new DataTemplate(() => MauiProgram.AppServices.GetService<AccountPage>()!),
            _                => content.ContentTemplate
        };
    }
}