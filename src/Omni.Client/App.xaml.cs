using Omni.Client.Abstractions;

namespace Omni.Client;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell()) { Title = "Omni" };
        _ = PreloadStoredTokenAsync();
        StartBackgroundServices();
        return window;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var message = (e.ExceptionObject as Exception)?.Message ?? e.ExceptionObject?.ToString() ?? "Unknown error";
        ShowExceptionPopup("Unexpected Error", message);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        var message = e.Exception?.GetBaseException()?.Message ?? e.Exception?.Message ?? "Unknown error";
        ShowExceptionPopup("Background Error", message);
    }

    private void ShowExceptionPopup(string title, string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var page = Windows.Count > 0 ? Windows[0].Page : null;
                if (page != null)
                    await page.DisplayAlertAsync(title, message, "OK");
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"[App] Unhandled: {title} — {message}");
            }
        });
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

            var notificationManager = MauiProgram.AppServices?.GetService<INotificationManager>();
            if (notificationManager != null)
                _ = notificationManager.RequestPermissionAsync();

            var usage = MauiProgram.AppServices?.GetService<IUsageService>();
            usage?.StartPeriodicSync();

            var sync = MauiProgram.AppServices?.GetService<ISyncService>();
            sync?.StartPeriodicSync();
        }
        catch { }
    }

    private static async Task PreloadStoredTokenAsync()
    {
        try
        {
            var auth = MauiProgram.AppServices?.GetService<IAuthService>();
            if (auth != null)
                await auth.GetTokenAsync();
        }
        catch { }
    }
}
