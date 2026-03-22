using System.Linq;

namespace Omni.Client;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
        Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));
        Routing.RegisterRoute(nameof(DigestPage), typeof(DigestPage));
        WireShellContentToServiceProvider();
    }

    private void WireShellContentToServiceProvider()
    {
        if (MauiProgram.AppServices == null) return;

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
            "ChatPage"       => new DataTemplate(() => MauiProgram.AppServices.GetService<ChatPage>()!),
            "SessionPage"    => new DataTemplate(() => MauiProgram.AppServices.GetService<SessionPage>()!),
            "TasksPage"      => new DataTemplate(() => MauiProgram.AppServices.GetService<TasksPage>()!),
            "CalendarPage"   => new DataTemplate(() => MauiProgram.AppServices.GetService<CalendarPage>()!),
            "AccountPage"    => new DataTemplate(() => MauiProgram.AppServices.GetService<AccountPage>()!),
            _                => content.ContentTemplate
        };
    }
}
