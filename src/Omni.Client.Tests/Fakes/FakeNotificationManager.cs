using Omni.Client.Abstractions;

namespace Omni.Client.Tests.Fakes;

/// <summary>Records notifications sent during a test without touching OS APIs.</summary>
public sealed class FakeNotificationManager : INotificationManager
{
    public List<(string Title, string Body)> Sent { get; } = new();

    public Task RequestPermissionAsync() => Task.CompletedTask;

    public void SendNotification(string title, string body) =>
        Sent.Add((title, body));
}
