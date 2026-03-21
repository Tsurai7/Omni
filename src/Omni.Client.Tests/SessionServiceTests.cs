using System.Net;
using Omni.Client.Models.Session;
using Omni.Client.Services;
using Omni.Client.Tests.Fakes;
using Omni.Client.Tests.Helpers;
using Xunit;

namespace Omni.Client.Tests;

public sealed class SessionServiceTests : IAsyncLifetime
{
    private readonly MockHttpMessageHandler _handler;
    private readonly FakeAuthService _auth;
    private readonly LocalDatabaseService _db;
    private readonly SessionService _svc;

    public SessionServiceTests()
    {
        var (client, handler) = TestHttpClientFactory.Create();
        _handler = handler;
        _auth = new FakeAuthService { Token = JwtHelper.ValidToken };
        _db = new LocalDatabaseService($"file:testdb_{Guid.NewGuid():N}?mode=memory&cache=shared");
        _svc = new SessionService(client, _auth, TestHttpClientFactory.JsonOptions, _db);
    }

    public async Task InitializeAsync() => await _db.InitializeAsync();
    public async Task DisposeAsync() => await _db.CloseAsync();

    // ── SyncSessionsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task SyncSessions_EmptyList_ReturnsTrueWithoutHttp()
    {
        var ok = await _svc.SyncSessionsAsync(Array.Empty<SessionSyncEntry>());
        Assert.True(ok);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SyncSessions_NoToken_ReturnsFalse()
    {
        _auth.Token = null;
        var entries = new[] { new SessionSyncEntry("Work", "work", "2024-01-01T09:00:00Z", 3600) };

        var ok = await _svc.SyncSessionsAsync(entries);

        Assert.False(ok);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SyncSessions_Success_ReturnsTrue()
    {
        _handler.Respond(HttpStatusCode.OK, "{}");
        var entries = new[] { new SessionSyncEntry("Work", "work", "2024-01-01T09:00:00Z", 3600) };

        var ok = await _svc.SyncSessionsAsync(entries);

        Assert.True(ok);
        Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, _handler.Requests[0].Method);
        Assert.Contains("sessions/sync", _handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task SyncSessions_Unauthorized_LogsOutAndSavesToPending()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, "{}");
        var entries = new[] { new SessionSyncEntry("Work", "work", "2024-01-01T09:00:00Z", 1800) };

        var ok = await _svc.SyncSessionsAsync(entries);

        Assert.False(ok);
        Assert.True(_auth.LoggedOut);
        // Should be queued for later
        var pending = await _db.GetUnsyncedPendingAsync();
        Assert.Single(pending);
        Assert.Equal("session", pending[0].Kind);
    }

    [Fact]
    public async Task SyncSessions_ServerError_SavesToPendingAndReturnsFalse()
    {
        _handler.Respond(HttpStatusCode.InternalServerError);
        var entries = new[] { new SessionSyncEntry("Coding", "work", "2024-01-01T09:00:00Z", 900) };

        var ok = await _svc.SyncSessionsAsync(entries);

        Assert.False(ok);
        var pending = await _db.GetUnsyncedPendingAsync();
        Assert.Single(pending);
    }

    [Fact]
    public async Task SyncSessions_NetworkError_SavesToPendingAndReturnsFalse()
    {
        _handler.RespondWithNetworkError();
        var entries = new[] { new SessionSyncEntry("Deep Work", "work", "2024-01-01T10:00:00Z", 7200) };

        var ok = await _svc.SyncSessionsAsync(entries);

        Assert.False(ok);
        var pending = await _db.GetUnsyncedPendingAsync();
        Assert.Single(pending);
    }

    [Fact]
    public async Task SyncSessions_SendsAuthorizationHeader()
    {
        _handler.Respond(HttpStatusCode.OK, "{}");
        var entries = new[] { new SessionSyncEntry("Work", "work", "2024-01-01T09:00:00Z", 3600) };

        await _svc.SyncSessionsAsync(entries);

        var authHeader = _handler.Requests[0].Headers.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader!.Scheme);
    }

    // ── GetSessionsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetSessions_NoToken_ReturnsNull()
    {
        _auth.Token = null;
        var result = await _svc.GetSessionsAsync();
        Assert.Null(result);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task GetSessions_Success_ReturnsSessionList()
    {
        _handler.Respond(HttpStatusCode.OK,
            """{"entries":[{"id":"s1","name":"Coding sprint","activity_type":"work","started_at":"2024-01-01T09:00:00Z","duration_seconds":3600}]}""");

        var result = await _svc.GetSessionsAsync();

        Assert.NotNull(result);
        Assert.Single(result!.Entries);
        Assert.Equal("Coding sprint", result.Entries[0].Name);
    }

    [Fact]
    public async Task GetSessions_WithDateRange_BuildsCorrectUrl()
    {
        _handler.Respond(HttpStatusCode.OK, """{"entries":[]}""");

        await _svc.GetSessionsAsync("2024-01-01", "2024-01-31");

        var uri = _handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("from=", uri);
        Assert.Contains("to=", uri);
        Assert.Contains("2024-01-01", uri);
        Assert.Contains("2024-01-31", uri);
    }

    [Fact]
    public async Task GetSessions_Unauthorized_LogsOutAndReturnsNull()
    {
        _handler.Respond(HttpStatusCode.Unauthorized);

        var result = await _svc.GetSessionsAsync();

        Assert.Null(result);
        Assert.True(_auth.LoggedOut);
    }

    [Fact]
    public async Task GetSessions_NetworkError_ReturnsNull()
    {
        _handler.RespondWithNetworkError();

        var result = await _svc.GetSessionsAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSessions_MalformedJson_ReturnsNull()
    {
        _handler.Respond(HttpStatusCode.OK, "{{not valid json}}");

        var result = await _svc.GetSessionsAsync();

        Assert.Null(result);
    }
}
