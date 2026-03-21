using System.Net;
using Omni.Client.Services;
using Omni.Client.Tests.Fakes;
using Omni.Client.Tests.Helpers;
using Xunit;

namespace Omni.Client.Tests;

public sealed class ProductivityServiceTests
{
    private static (ProductivityService Service, MockHttpMessageHandler Handler, FakeAuthService Auth) Build()
    {
        var (client, handler) = TestHttpClientFactory.Create();
        var auth = new FakeAuthService { Token = JwtHelper.ValidToken };
        var svc  = new ProductivityService(client, auth, TestHttpClientFactory.JsonOptions);
        return (svc, handler, auth);
    }

    // ── GetNotificationsAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_NoToken_ReturnsEmpty()
    {
        var (svc, _, auth) = Build();
        auth.Token = null;

        var result = await svc.GetNotificationsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNotifications_AllNotifications_ReturnsList()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """
            {"items":[
                {"id":"n1","created_at":"2024-01-01","type":"recommendation",
                 "title":"Take a break","body":"You've been working for 90 min.",
                 "action_type":null,"action_payload":null,"read_at":null},
                {"id":"n2","created_at":"2024-01-02","type":"insight",
                 "title":"Focus trend","body":"You focus best before noon.",
                 "action_type":null,"action_payload":null,"read_at":"2024-01-02"}
            ]}
            """);

        var result = await svc.GetNotificationsAsync(unreadOnly: false);

        Assert.Equal(2, result.Count);
        Assert.Equal("recommendation", result[0].Type);
        Assert.Equal("n2", result[1].Id);
        // URL should contain unread_only=false
        Assert.Contains("unread_only=false", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetNotifications_UnreadOnly_SendsCorrectQueryParam()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """{"items":[]}""");

        await svc.GetNotificationsAsync(unreadOnly: true);

        Assert.Contains("unread_only=true", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetNotifications_Unauthorized_ReturnsEmpty()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.Unauthorized);

        var result = await svc.GetNotificationsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNotifications_NetworkError_ReturnsEmpty()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var result = await svc.GetNotificationsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNotifications_ServerError_ReturnsEmpty()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.InternalServerError);

        var result = await svc.GetNotificationsAsync();

        Assert.Empty(result);
    }

    // ── MarkAsReadAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task MarkAsRead_EmptyId_DoesNotSendRequest()
    {
        var (svc, handler, _) = Build();

        await svc.MarkAsReadAsync("   ");

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MarkAsRead_NoToken_DoesNotSendRequest()
    {
        var (svc, handler, auth) = Build();
        auth.Token = null;

        await svc.MarkAsReadAsync("n1");

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MarkAsRead_Success_SendsPatchRequest()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, "{}");

        await svc.MarkAsReadAsync("notif-1");

        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, handler.Requests[0].Method);
        Assert.Contains("notif-1", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("/read", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task MarkAsRead_NetworkError_DoesNotThrow()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var ex = await Record.ExceptionAsync(() => svc.MarkAsReadAsync("n1"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task MarkAsRead_Unauthorized_DoesNotThrow()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.Unauthorized);

        var ex = await Record.ExceptionAsync(() => svc.MarkAsReadAsync("n1"));
        Assert.Null(ex);
    }
}
