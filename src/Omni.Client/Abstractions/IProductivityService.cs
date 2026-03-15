using Omni.Client.Models.Productivity;

namespace Omni.Client.Abstractions;

public interface IProductivityService
{
    Task<List<NotificationItem>> GetNotificationsAsync(bool unreadOnly = false, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(string notificationId, CancellationToken cancellationToken = default);
}
