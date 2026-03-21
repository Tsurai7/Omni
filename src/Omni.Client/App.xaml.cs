using Omni.Client.Abstractions;

namespace Omni.Client;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Global exception handlers: catch anything that escapes task/event handlers
        // and show a user-friendly popup instead of a silent crash.
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

    // ── Global exception handlers ──────────────────────────────────────────

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var message = (e.ExceptionObject as Exception)?.Message ?? e.ExceptionObject?.ToString() ?? "Unknown error";
        ShowExceptionPopup("Unexpected Error", message);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved(); // prevent process termination
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
                    await page.DisplayAlert(title, message, "OK");
            }
            catch
            {
                // If we cannot show a dialog, at minimum log – better than crashing the handler
                System.Diagnostics.Debug.WriteLine($"[App] Unhandled: {title} — {message}");
            }
        });
    }

    // ── Startup ───────────────────────────────────────────────────────────

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
