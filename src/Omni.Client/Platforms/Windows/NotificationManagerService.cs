using Microsoft.Toolkit.Uwp.Notifications;
using Omni.Client.Abstractions;

namespace Omni.Client.Platforms.Windows;

public sealed class NotificationManagerService : INotificationManager
{
    public void SendNotification(string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows notification failed: {ex.Message}");
        }
    }

    public Task RequestPermissionAsync()
    {
        // Windows 10/11 does not require runtime permission for local toast notifications.
        return Task.CompletedTask;
    }
}
