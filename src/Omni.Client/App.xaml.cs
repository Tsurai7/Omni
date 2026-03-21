using Omni.Client.Abstractions;

namespace Omni.Client;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell()) { Title = "Omni" };
        _ = PreloadStoredTokenAsync();
        StartBackgroundServices();
        return window;
    }

    private static void StartBackgroundServices()
    {
        try
        {
            var localDb = MauiProgram.AppServices?.GetService<Services.LocalDatabaseService>();
            if (localDb != null)
                _ = localDb.InitializeAsync();

            var tracker = MauiProgram.AppServices?.GetService<IActiveWindowTracker>();
            tracker?.StartTracking();

            // Request notification permission at startup so distraction alerts can show without delay.
            var notificationManager = MauiProgram.AppServices?.GetService<INotificationManager>();
            if (notificationManager != null)
                _ = notificationManager.RequestPermissionAsync();

            var usage = MauiProgram.AppServices?.GetService<IUsageService>();
            usage?.StartPeriodicSync();

            var sync = MauiProgram.AppServices?.GetService<ISyncService>();
            sync?.StartPeriodicSync();
        }
        catch
        {
            // Non-fatal; tracking/sync can start when MainPage appears as fallback
        }
    }

    private static async Task PreloadStoredTokenAsync()
    {
        try
        {
            var auth = MauiProgram.AppServices?.GetService<IAuthService>();
            if (auth != null)
                await auth.GetTokenAsync();
        }
        catch
        {
            // Non-fatal; auth check will run again when MainPage appears
        }
    }
}