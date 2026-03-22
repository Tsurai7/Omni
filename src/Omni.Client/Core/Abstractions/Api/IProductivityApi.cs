using Omni.Client.Models.Productivity;
using Refit;

namespace Omni.Client.Core.Abstractions.Api;

public interface IProductivityApi
{
    [Get("/api/productivity/notifications")]
    Task<NotificationsResponse> GetNotificationsAsync(
        [AliasAs("unread_only")] bool unreadOnly = false,
        CancellationToken ct = default);

    [Patch("/api/productivity/notifications/{id}/read")]
    Task MarkAsReadAsync(string id, CancellationToken ct = default);
}
