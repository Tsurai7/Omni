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
        var window = new Window(new AppShell());
        _ = PreloadStoredTokenAsync();
        StartBackgroundServices();
        return window;
    }

    private static void StartBackgroundServices()
    {
        try
        {
            var tracker = MauiProgram.AppServices?.GetService<IActiveWindowTracker>();
            tracker?.StartTracking();

            var usage = MauiProgram.AppServices?.GetService<IUsageService>();
            usage?.StartPeriodicSync();
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