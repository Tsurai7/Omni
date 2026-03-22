using System.Diagnostics;
using Omni.Client.Abstractions;
using Omni.Client.Core.Abstractions.Api;
using Omni.Client.Models.Productivity;
using Refit;

namespace Omni.Client.Services;

public sealed class ProductivityService : IProductivityService
{
    private readonly IProductivityApi _api;

    public ProductivityService(IProductivityApi api)
    {
        _api = api;
    }

    public async Task<List<NotificationItem>> GetNotificationsAsync(bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await _api.GetNotificationsAsync(unreadOnly, cancellationToken);
            return data?.Items ?? new List<NotificationItem>();
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"ProductivityService.GetNotificationsAsync: {ex.StatusCode}");
            return new List<NotificationItem>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ProductivityService.GetNotificationsAsync: {ex.Message}");
            return new List<NotificationItem>();
        }
    }

    public async Task MarkAsReadAsync(string notificationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(notificationId))
            return;

        try
        {
            await _api.MarkAsReadAsync(notificationId, cancellationToken);
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"ProductivityService.MarkAsReadAsync: {ex.StatusCode}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ProductivityService.MarkAsReadAsync: {ex.Message}");
        }
    }
}
