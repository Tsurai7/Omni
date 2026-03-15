using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Models.Productivity;

namespace Omni.Client.Services;

public sealed class ProductivityService : IProductivityService
{
    private readonly HttpClient _http;
    private readonly IAuthService _authService;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProductivityService(HttpClient http, IAuthService authService, JsonSerializerOptions jsonOptions)
    {
        _http = http;
        _authService = authService;
        _jsonOptions = jsonOptions;
    }

    public async Task<List<NotificationItem>> GetNotificationsAsync(bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
            return new List<NotificationItem>();

        var url = "api/productivity/notifications?unread_only=" + (unreadOnly ? "true" : "false");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Debug.WriteLine("ProductivityService.GetNotificationsAsync: 401");
                return new List<NotificationItem>();
            }
            if (!response.IsSuccessStatusCode)
                return new List<NotificationItem>();

            var data = await response.Content.ReadFromJsonAsync<NotificationsResponse>(_jsonOptions, cancellationToken);
            return data?.Items ?? new List<NotificationItem>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ProductivityService.GetNotificationsAsync: {0}", ex.Message);
            return new List<NotificationItem>();
        }
    }

    public async Task MarkAsReadAsync(string notificationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(notificationId))
            return;

        var token = await _authService.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
            return;

        var request = new HttpRequestMessage(HttpMethod.Patch, $"api/productivity/notifications/{notificationId}/read");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return;
            // Ignore 404; no need to surface
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ProductivityService.MarkAsReadAsync: {0}", ex.Message);
        }
    }
}
