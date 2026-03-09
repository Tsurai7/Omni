using Foundation;
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
        UNUserNotificationCenter.Current.RequestAuthorization(UNAuthorizationOptions.Alert, (approved, err) =>
        {
            _hasPermission = approved;
            if (approved)
                DoAddNotification(title, body);
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
                System.Diagnostics.Debug.WriteLine($"MacCatalyst notification failed: {err}");
        });
    }

    public async Task RequestPermissionAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        UNUserNotificationCenter.Current.RequestAuthorization(UNAuthorizationOptions.Alert, (approved, err) =>
        {
            _hasPermission = approved;
            tcs.TrySetResult(approved);
        });
        await tcs.Task;
    }
}
