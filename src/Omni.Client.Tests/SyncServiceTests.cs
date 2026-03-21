using System.Net;
using System.Text.Json;
using Omni.Client.Models.Task;
using Omni.Client.Models.Usage;
using Omni.Client.Models.Session;
using Omni.Client.Services;
using Omni.Client.Tests.Fakes;
using Omni.Client.Tests.Helpers;
using Xunit;

namespace Omni.Client.Tests;

public sealed class SyncServiceTests : IAsyncLifetime
{
    private readonly MockHttpMessageHandler _handler;
    private readonly FakeAuthService _auth;
    private readonly LocalDatabaseService _db;
    private readonly SyncService _svc;
    private static readonly JsonSerializerOptions _json = TestHttpClientFactory.JsonOptions;

    public SyncServiceTests()
    {
        var (client, handler) = TestHttpClientFactory.Create();
        _handler = handler;
        _auth = new FakeAuthService { Token = JwtHelper.ValidToken };
        _db = new LocalDatabaseService($"file:testdb_{Guid.NewGuid():N}?mode=memory&cache=shared");
        _svc = new SyncService(client, _auth, _db, _json);
    }

    public async Task InitializeAsync() => await _db.InitializeAsync();
    public async Task DisposeAsync() => await _db.CloseAsync();

    // ── RunSyncOnceAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RunSyncOnce_NoToken_SkipsEverything()
    {
        _auth.Token = null;
        // Seed a pending row to verify it is not processed
        await _db.SavePendingSyncAsync("usage", """{"entries":[]}""");

        await _svc.RunSyncOnceAsync();

        Assert.Empty(_handler.Requests);
        // Row still unsynced
        var pending = await _db.GetUnsyncedPendingAsync();
        Assert.Single(pending);
        Assert.False(pending[0].IsSynced);
    }

    [Fact]
    public async Task RunSyncOnce_NoRows_SendsNoRequests()
    {
        await _svc.RunSyncOnceAsync();
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task RunSyncOnce_DrainsPendingUsageRow_MarksAsSynced()
    {
        var payload = JsonSerializer.Serialize(
            new UsageSyncRequest(new List<UsageSyncEntry>
            {
                new("Chrome", "Productivity", 3600)
            }), _json);
        await _db.SavePendingSyncAsync("usage", payload);

        _handler.Respond(HttpStatusCode.OK, "{}"); // usage/sync response

        await _svc.RunSyncOnceAsync();

        Assert.Single(_handler.Requests);
        Assert.Contains("usage/sync", _handler.Requests[0].RequestUri!.ToString());

        var remaining = await _db.GetUnsyncedPendingAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task RunSyncOnce_DrainsPendingSessionRow_MarksAsSynced()
    {
        var payload = JsonSerializer.Serialize(
            new SessionSyncRequest
            {
                Entries = new List<SessionSyncEntry> { new("Work", "work", "2024-01-01T09:00:00Z", 3600) }
            }, _json);
        await _db.SavePendingSyncAsync("session", payload);

        _handler.Respond(HttpStatusCode.OK, "{}");

        await _svc.RunSyncOnceAsync();

        Assert.Contains("sessions/sync", _handler.Requests[0].RequestUri!.ToString());
        Assert.Empty(await _db.GetUnsyncedPendingAsync());
    }

    [Fact]
    public async Task RunSyncOnce_PendingRowFails_RemainsUnsynced()
    {
        var payload = JsonSerializer.Serialize(
            new UsageSyncRequest(new List<UsageSyncEntry> { new("App", "Other", 100) }), _json);
        await _db.SavePendingSyncAsync("usage", payload);

        _handler.Respond(HttpStatusCode.ServiceUnavailable);

        await _svc.RunSyncOnceAsync();

        // Should still be unsynced
        var remaining = await _db.GetUnsyncedPendingAsync();
        Assert.Single(remaining);
        Assert.False(remaining[0].IsSynced);
    }

    [Fact]
    public async Task RunSyncOnce_UnsyncedTask_CreatesOnServer()
    {
        var task = new LocalTask
        {
            Id = "local-1", Title = "Write tests", Status = "pending",
            Priority = "high", IsSynced = false,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await _db.InsertTaskAsync(task);

        _handler.Respond(HttpStatusCode.OK,
            """{"id":"srv-123","title":"Write tests","status":"pending","priority":"high","created_at":"2024-01-01","updated_at":"2024-01-01","user_id":"u"}""");

        await _svc.RunSyncOnceAsync();

        Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, _handler.Requests[0].Method);
        Assert.Contains("api/tasks", _handler.Requests[0].RequestUri!.ToString());

        // Task should be marked synced
        var tasks = await _db.GetUnsyncedTasksAsync();
        Assert.Empty(tasks);
    }

    [Fact]
    public async Task RunSyncOnce_TaskWithServerId_PatchesStatus()
    {
        var task = new LocalTask
        {
            Id = "local-2", ServerId = "srv-456", Title = "Review PR",
            Status = "done", Priority = "medium", IsSynced = false,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await _db.InsertTaskAsync(task);

        _handler.Respond(HttpStatusCode.OK, "{}");

        await _svc.RunSyncOnceAsync();

        Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Patch, _handler.Requests[0].Method);
        Assert.Contains("srv-456", _handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task RunSyncOnce_ConcurrentCalls_SecondIsNoOp()
    {
        // Queue a response for the first call only
        _handler.Respond(HttpStatusCode.OK, "{}");
        await _db.SavePendingSyncAsync("usage",
            JsonSerializer.Serialize(new UsageSyncRequest(new List<UsageSyncEntry> { new("App", "Other", 100) }), _json));

        // Fire two concurrent syncs
        var t1 = _svc.RunSyncOnceAsync();
        var t2 = _svc.RunSyncOnceAsync();
        await Task.WhenAll(t1, t2);

        // Only one HTTP call should have been made (second call skipped via semaphore)
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task RunSyncOnce_InvalidPendingPayload_SkipsRow()
    {
        await _db.SavePendingSyncAsync("usage", "not valid json{{");

        await _svc.RunSyncOnceAsync(); // should not throw

        // No HTTP request should have been sent for a corrupted payload
        Assert.Empty(_handler.Requests);
    }
}
