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
        // Pre-load stored token so first auth check (e.g. MainPage.OnAppearing) sees it without race
        _ = PreloadStoredTokenAsync();
        return window;
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