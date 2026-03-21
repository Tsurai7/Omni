using Omni.Client.Abstractions;

namespace Omni.Client.Controls;

/// <summary>
/// Sidebar footer — avatar circle + user email. Loaded once on first appearance.
/// </summary>
public partial class SidebarFooter : ContentView
{
    private bool _loaded;

    public SidebarFooter()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var authService = MauiProgram.AppServices?.GetService<IAuthService>();
            if (authService == null) return;

            var user = await authService.GetCurrentUserAsync();
            var email = user?.Email ?? string.Empty;

            EmailLabel.Text = string.IsNullOrEmpty(email) ? "—" : email;

            if (!string.IsNullOrEmpty(email))
                AvatarInitials.Text = email[0].ToString().ToUpperInvariant();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SidebarFooter.OnLoaded: {ex.Message}");
        }
    }
}
