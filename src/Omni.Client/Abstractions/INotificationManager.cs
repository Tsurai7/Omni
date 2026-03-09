namespace Omni.Client.Abstractions;

/// <summary>
/// Sends local notifications to the user (e.g. distraction alerts).
/// </summary>
public interface INotificationManager
{
    /// <summary>Send a local notification. May request permission on first use.</summary>
    void SendNotification(string title, string body);

    /// <summary>Request notification permission if required by the platform. Call before first session with distraction tracking.</summary>
    Task RequestPermissionAsync();
}
