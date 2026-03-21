using System.Net;
using Omni.Client.Services;
using Omni.Client.Tests.Fakes;
using Omni.Client.Tests.Helpers;
using Xunit;

namespace Omni.Client.Tests;

public sealed class CalendarServiceTests
{
    private static (CalendarService Service, MockHttpMessageHandler Handler, FakeAuthService Auth) Build()
    {
        var (client, handler) = TestHttpClientFactory.Create();
        var auth = new FakeAuthService { Token = JwtHelper.ValidToken };
        var svc  = new CalendarService(client, auth, TestHttpClientFactory.JsonOptions);
        return (svc, handler, auth);
    }

    // ── GetAuthUrlAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAuthUrl_NoToken_ReturnsNull()
    {
        var (svc, _, auth) = Build();
        auth.Token = null;

        var url = await svc.GetAuthUrlAsync();

        Assert.Null(url);
    }

    [Fact]
    public async Task GetAuthUrl_Success_ReturnsOAuthUrl()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """{"url":"https://accounts.google.com/o/oauth2/auth?..."}""");

        var url = await svc.GetAuthUrlAsync();

        Assert.NotNull(url);
        Assert.Contains("google.com", url);
    }

    [Fact]
    public async Task GetAuthUrl_ServerError_ReturnsNull()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.Unauthorized);

        var url = await svc.GetAuthUrlAsync();

        Assert.Null(url);
    }

    [Fact]
    public async Task GetAuthUrl_NetworkError_ReturnsNull()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var url = await svc.GetAuthUrlAsync();

        Assert.Null(url);
    }

    // ── ConnectAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task Connect_NoToken_ReturnsFalse()
    {
        var (svc, _, auth) = Build();
        auth.Token = null;

        var ok = await svc.ConnectAsync("oauth-code");

        Assert.False(ok);
    }

    [Fact]
    public async Task Connect_Success_ReturnsTrueAndUpdatesStatus()
    {
        var (svc, handler, _) = Build();
        // First call: connect endpoint; second call: RefreshStatusAsync inside Connect
        handler.Respond(HttpStatusCode.OK, "{}");
        handler.Respond(HttpStatusCode.OK, """{"connected":true,"email":"me@gmail.com","last_synced_at":null}""");

        var ok = await svc.ConnectAsync("auth-code-123");

        Assert.True(ok);
        Assert.True(svc.IsConnected);
    }

    [Fact]
    public async Task Connect_Failure_ReturnsFalse()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.BadRequest);

        var ok = await svc.ConnectAsync("bad-code");

        Assert.False(ok);
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public async Task Connect_NetworkError_ReturnsFalse()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var ok = await svc.ConnectAsync("code");

        Assert.False(ok);
    }

    // ── DisconnectAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task Disconnect_NoToken_ReturnsFalse()
    {
        var (svc, _, auth) = Build();
        auth.Token = null;

        var ok = await svc.DisconnectAsync();

        Assert.False(ok);
    }

    [Fact]
    public async Task Disconnect_Success_ClearsConnectionState()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, "{}");

        var ok = await svc.DisconnectAsync();

        Assert.True(ok);
        Assert.False(svc.IsConnected);
        Assert.Null(svc.ConnectedEmail);
        Assert.Null(svc.LastSyncedAt);
    }

    [Fact]
    public async Task Disconnect_NetworkError_ReturnsFalseAndClearsState()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var ok = await svc.DisconnectAsync();

        Assert.False(ok);
        // State should still be cleared on disconnect attempt
        Assert.False(svc.IsConnected);
    }

    // ── RefreshStatusAsync ──────────────────────────────────────────────

    [Fact]
    public async Task RefreshStatus_Success_UpdatesLocalState()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK,
            """{"connected":true,"email":"me@gmail.com","last_synced_at":"2024-01-15T10:00:00Z"}""");

        var status = await svc.RefreshStatusAsync();

        Assert.NotNull(status);
        Assert.True(svc.IsConnected);
        Assert.Equal("me@gmail.com", svc.ConnectedEmail);
        Assert.NotNull(svc.LastSyncedAt);
    }

    [Fact]
    public async Task RefreshStatus_NotConnected_ReflectsDisconnectedState()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK,
            """{"connected":false,"email":null,"last_synced_at":null}""");

        var status = await svc.RefreshStatusAsync();

        Assert.NotNull(status);
        Assert.False(svc.IsConnected);
        Assert.Null(svc.ConnectedEmail);
    }

    [Fact]
    public async Task RefreshStatus_NetworkError_ReturnsNull()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var result = await svc.RefreshStatusAsync();

        Assert.Null(result);
    }

    // ── GetEventsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetEvents_NoToken_ReturnsEmpty()
    {
        var (svc, _, auth) = Build();
        auth.Token = null;

        var events = await svc.GetEventsAsync(DateTime.Today, DateTime.Today.AddDays(7));

        Assert.Empty(events);
    }

    [Fact]
    public async Task GetEvents_Success_ReturnsEventList()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """
            {"events":[
                {"id":"e1","title":"Team standup","start_at":"2024-03-20T09:00:00Z",
                 "end_at":"2024-03-20T09:30:00Z","is_all_day":false,"source":"google_calendar"},
                {"id":"e2","title":"Deploy","start_at":"2024-03-20T14:00:00Z",
                 "end_at":null,"is_all_day":false,"source":"omni_task","priority":"high"}
            ]}
            """);

        var events = await svc.GetEventsAsync(DateTime.Today, DateTime.Today.AddDays(7));

        Assert.Equal(2, events.Count);
        Assert.Equal("Team standup", events[0].Title);
        Assert.True(events[0].IsGoogleCalendar);
        Assert.Equal("Deploy", events[1].Title);
        Assert.True(events[1].IsOmniTask);
    }

    [Fact]
    public async Task GetEvents_RequestUrlContainsDateRange()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """{"events":[]}""");

        var start = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = new DateTime(2024, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        await svc.GetEventsAsync(start, end);

        var url = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("start=", url);
        Assert.Contains("end=", url);
    }

    [Fact]
    public async Task GetEvents_NetworkError_ReturnsEmpty()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var events = await svc.GetEventsAsync(DateTime.Today, DateTime.Today.AddDays(1));

        Assert.Empty(events);
    }

    // ── CreateGoogleEventAsync ──────────────────────────────────────────

    [Fact]
    public async Task CreateGoogleEvent_Success_ReturnsTrueAndTriggerSync()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """{"id":"ge1"}"""); // create
        handler.Respond(HttpStatusCode.OK, "{}");               // sync triggered by CreateGoogleEventAsync

        var ok = await svc.CreateGoogleEventAsync(
            "Team meeting", DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2),
            isAllDay: false);

        Assert.True(ok);
        Assert.Equal(2, handler.Requests.Count); // create + sync
    }

    [Fact]
    public async Task CreateGoogleEvent_Failure_ReturnsFalse()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.Forbidden);

        var ok = await svc.CreateGoogleEventAsync("Meeting", DateTime.UtcNow, null, false);

        Assert.False(ok);
    }

    // ── SyncAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_Success_ReturnsTrueAndUpdatesLastSyncedAt()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, "{}");

        var before = DateTime.Now;
        var ok = await svc.SyncAsync();

        Assert.True(ok);
        Assert.NotNull(svc.LastSyncedAt);
        Assert.True(svc.LastSyncedAt >= before);
    }

    [Fact]
    public async Task Sync_NetworkError_ReturnsFalse()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var ok = await svc.SyncAsync();

        Assert.False(ok);
    }

    // ── StatusChanged event ─────────────────────────────────────────────

    [Fact]
    public async Task Disconnect_RaisesStatusChangedEvent()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, "{}");

        bool eventFired = false;
        svc.StatusChanged += (_, _) => eventFired = true;

        await svc.DisconnectAsync();

        Assert.True(eventFired);
    }
}
