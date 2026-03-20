using Foundation;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Omni.Client.Abstractions;
using UserNotifications;

namespace Omni.Client.Platforms.MacCatalyst;

public sealed class NotificationManagerService : INotificationManager
{
    private int _messageId;
    private bool _hasPermission;

    public NotificationManagerService()
    {
        // Request at startup so permission may be ready before first notification.
        _ = RequestPermissionAsync();
    }

    public void SendNotification(string title, string body)
    {
        if (_hasPermission)
        {
            DoAddNotification(title, body);
            return;
        }

        // Permission not yet granted (e.g. callback not run). Request and send from callback so we don't drop the notification.
        System.Diagnostics.Debug.WriteLine("[NotificationManager] Permission not yet granted; requesting and will send from callback.");
        UNUserNotificationCenter.Current.RequestAuthorization(UNAuthorizationOptions.Alert, (approved, err) =>
        {
            _hasPermission = approved;
            if (err != null)
                System.Diagnostics.Debug.WriteLine($"[NotificationManager] RequestAuthorization error: {err}");
            if (approved)
            {
                System.Diagnostics.Debug.WriteLine("[NotificationManager] Permission granted; sending notification.");
                DoAddNotification(title, body);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[NotificationManager] Permission denied; notification not shown. Showing in-app fallback.");
                ShowInAppFallback(title, body);
            }
        });
    }

    private void DoAddNotification(string title, string body)
    {
        _messageId++;
        var content = new UNMutableNotificationContent
        {
            Title = title,
            Body = body,
            Sound = UNNotificationSound.Default
        };

        var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(0.25, false);
        var request = UNNotificationRequest.FromIdentifier(_messageId.ToString(), content, trigger);
        UNUserNotificationCenter.Current.AddNotificationRequest(request, err =>
        {
            if (err != null)
                System.Diagnostics.Debug.WriteLine($"[NotificationManager] AddNotificationRequest failed: {err.LocalizedDescription}");
            else
                System.Diagnostics.Debug.WriteLine($"[NotificationManager] Notification scheduled: \"{title}\"");
        });
    }

    public async Task RequestPermissionAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        UNUserNotificationCenter.Current.RequestAuthorization(UNAuthorizationOptions.Alert, (approved, err) =>
        {
            _hasPermission = approved;
            if (err != null)
                System.Diagnostics.Debug.WriteLine($"[NotificationManager] RequestAuthorization error at startup: {err.LocalizedDescription} (code={(long)err.Code})");
            tcs.TrySetResult(approved);
        });
        await tcs.Task;
    }

    /// <summary>Show the notification content in an in-app alert when system permission is denied.</summary>
    private static void ShowInAppFallback(string title, string body)
    {
        var window = Application.Current?.Windows?.FirstOrDefault();
        var page = window?.Page;
        if (page == null) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await page.DisplayAlertAsync(title, body, "OK");
        });
    }
}
