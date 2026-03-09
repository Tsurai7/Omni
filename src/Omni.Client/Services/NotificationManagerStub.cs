using Omni.Client.Abstractions;

namespace Omni.Client.Services;

/// <summary>
/// No-op notification manager when platform implementations are not available.
/// </summary>
public sealed class NotificationManagerStub : INotificationManager
{
    public void SendNotification(string title, string body)
    {
        System.Diagnostics.Debug.WriteLine($"[Notification] {title}: {body}");
    }

    public Task RequestPermissionAsync() => Task.CompletedTask;
}
