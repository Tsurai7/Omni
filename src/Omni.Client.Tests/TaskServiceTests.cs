using System.Net;
using Omni.Client.Services;
using Omni.Client.Tests.Fakes;
using Omni.Client.Tests.Helpers;
using Xunit;

namespace Omni.Client.Tests;

public sealed class TaskServiceTests : IAsyncLifetime
{
    private readonly MockHttpMessageHandler _handler;
    private readonly FakeAuthService _auth;
    private readonly LocalDatabaseService _db;
    private readonly TaskService _svc;

    public TaskServiceTests()
    {
        var (client, handler) = TestHttpClientFactory.Create();
        _handler = handler;
        _auth = new FakeAuthService { Token = JwtHelper.ValidToken };
        _db = new LocalDatabaseService($"file:testdb_{Guid.NewGuid():N}?mode=memory&cache=shared");
        _svc = new TaskService(client, _auth, TestHttpClientFactory.JsonOptions, _db);
    }

    public async Task InitializeAsync() => await _db.InitializeAsync();
    public async Task DisposeAsync() => await _db.CloseAsync();

    // ── GetTasksAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetTasks_NoToken_ReturnsEmptyList()
    {
        _auth.Token = null;
        var result = await _svc.GetTasksAsync();
        Assert.Empty(result);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task GetTasks_Success_ReturnsTaskList()
    {
        _handler.Respond(HttpStatusCode.OK, """
            {"tasks":[
                {"id":"1","user_id":"u","title":"Buy milk","status":"pending","priority":"medium","created_at":"2024-01-01","updated_at":"2024-01-01"},
                {"id":"2","user_id":"u","title":"Call dentist","status":"done","priority":"high","created_at":"2024-01-01","updated_at":"2024-01-01"}
            ]}
            """);

        var tasks = await _svc.GetTasksAsync();

        Assert.Equal(2, tasks.Count);
        Assert.Equal("Buy milk", tasks[0].Title);
        Assert.Equal("done", tasks[1].Status);
    }

    [Fact]
    public async Task GetTasks_Unauthorized_LogsOutAndReturnsEmpty()
    {
        _handler.Respond(HttpStatusCode.Unauthorized);

        var result = await _svc.GetTasksAsync();

        Assert.Empty(result);
        Assert.True(_auth.LoggedOut);
    }

    [Fact]
    public async Task GetTasks_NetworkError_ReturnsEmpty()
    {
        _handler.RespondWithNetworkError();

        var result = await _svc.GetTasksAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTasks_MalformedJson_ReturnsEmpty()
    {
        _handler.Respond(HttpStatusCode.OK, "not json at all {{");

        var result = await _svc.GetTasksAsync();

        Assert.Empty(result);
    }

    // ── CreateTaskAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateTask_OnlineSuccess_ReturnsServerResult()
    {
        _handler.Respond(HttpStatusCode.OK, """
            {"id":"srv-1","user_id":"u","title":"Buy groceries","status":"pending",
             "priority":"medium","created_at":"2024-01-01","updated_at":"2024-01-01"}
            """);

        var result = await _svc.CreateTaskAsync("Buy groceries");

        Assert.NotNull(result);
        Assert.Equal("srv-1", result.Id);
        Assert.Equal("Buy groceries", result.Title);
    }

    [Fact]
    public async Task CreateTask_EmptyTitle_ReturnsNull()
    {
        var result = await _svc.CreateTaskAsync("   ");
        Assert.Null(result);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task CreateTask_NetworkError_SavesLocally()
    {
        _handler.RespondWithNetworkError();

        var result = await _svc.CreateTaskAsync("Offline task");

        Assert.NotNull(result);
        Assert.Equal("Offline task", result.Title);
        Assert.Equal("pending", result.Status);
        // Should be persisted locally
        var localTasks = await _db.GetAllTasksAsync();
        Assert.Single(localTasks);
        Assert.Equal("Offline task", localTasks[0].Title);
        Assert.False(localTasks[0].IsSynced);
    }

    [Fact]
    public async Task CreateTask_NoToken_SavesLocally()
    {
        _auth.Token = null;

        var result = await _svc.CreateTaskAsync("No-token task", "high");

        Assert.NotNull(result);
        Assert.Equal("No-token task", result.Title);
        Assert.Equal("high", result.Priority);
        var localTasks = await _db.GetAllTasksAsync();
        Assert.Single(localTasks);
    }

    [Fact]
    public async Task CreateTask_WithDueDate_IncludesDateInRequest()
    {
        _handler.Respond(HttpStatusCode.OK, """
            {"id":"d1","user_id":"u","title":"Dentist","status":"pending","priority":"medium",
             "created_at":"2024-01-01","updated_at":"2024-01-01","due_date":"2024-03-15T00:00:00Z"}
            """);

        var dueDate = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var result = await _svc.CreateTaskAsync("Dentist", dueDate: dueDate);

        Assert.NotNull(result);
        var requestBody = await _handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("2024-03-15", requestBody);
    }

    // ── UpdateStatusAsync ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateStatus_Success_ReturnsTrue()
    {
        _handler.Respond(HttpStatusCode.OK, """{"status":"done"}""");

        var ok = await _svc.UpdateStatusAsync("task-1", "done");

        Assert.True(ok);
        Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Patch, _handler.Requests[0].Method);
    }

    [Fact]
    public async Task UpdateStatus_EmptyTaskId_ReturnsFalse()
    {
        var ok = await _svc.UpdateStatusAsync("", "done");
        Assert.False(ok);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task UpdateStatus_Unauthorized_LogsOut()
    {
        _handler.Respond(HttpStatusCode.Unauthorized);

        await _svc.UpdateStatusAsync("task-1", "done");

        Assert.True(_auth.LoggedOut);
    }

    [Fact]
    public async Task UpdateStatus_NetworkError_UpdatesLocalAndReturnsTrue()
    {
        // Seed a local task to update
        await _db.InsertTaskAsync(new Omni.Client.Models.Task.LocalTask
        {
            Id = "local-1", Title = "Local task", Status = "pending",
            Priority = "medium", IsSynced = false,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });

        _handler.RespondWithNetworkError();

        var ok = await _svc.UpdateStatusAsync("local-1", "done");

        Assert.True(ok);
    }

    // ── UpdateTaskAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateTask_Success_ReturnsTrue()
    {
        _handler.Respond(HttpStatusCode.OK, "{}");

        var ok = await _svc.UpdateTaskAsync("task-1", "New title", "high");

        Assert.True(ok);
        Assert.Equal(HttpMethod.Put, _handler.Requests[0].Method);
    }

    [Fact]
    public async Task UpdateTask_EmptyTitle_ReturnsFalse()
    {
        var ok = await _svc.UpdateTaskAsync("task-1", "  ", "medium");
        Assert.False(ok);
        Assert.Empty(_handler.Requests);
    }

    // ── DeleteTaskAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTask_Success_ReturnsTrue()
    {
        _handler.Respond(HttpStatusCode.OK);

        var ok = await _svc.DeleteTaskAsync("task-1");

        Assert.True(ok);
        Assert.Equal(HttpMethod.Delete, _handler.Requests[0].Method);
    }

    [Fact]
    public async Task DeleteTask_NoContent_ReturnsTrue()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        var ok = await _svc.DeleteTaskAsync("task-1");

        Assert.True(ok);
    }

    [Fact]
    public async Task DeleteTask_NotFound_DeletesLocally()
    {
        // Seed locally so DeleteAsync has something to act on
        await _db.InsertTaskAsync(new Omni.Client.Models.Task.LocalTask
        {
            Id = "local-1", Title = "Gone task", Status = "pending",
            Priority = "medium", IsSynced = false,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        _handler.Respond(HttpStatusCode.NotFound);

        var ok = await _svc.DeleteTaskAsync("local-1");

        Assert.True(ok);
        var remaining = await _db.GetAllTasksAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteTask_EmptyId_ReturnsFalse()
    {
        var ok = await _svc.DeleteTaskAsync("");
        Assert.False(ok);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task DeleteTask_NoToken_DeletesLocally()
    {
        _auth.Token = null;
        await _db.InsertTaskAsync(new Omni.Client.Models.Task.LocalTask
        {
            Id = "local-x", Title = "Unsynced", Status = "pending",
            Priority = "medium", IsSynced = false,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });

        var ok = await _svc.DeleteTaskAsync("local-x");

        Assert.True(ok);
        Assert.Empty(await _db.GetAllTasksAsync());
    }

    [Fact]
    public async Task DeleteTask_NetworkError_DeletesLocallyAndReturnsTrue()
    {
        await _db.InsertTaskAsync(new Omni.Client.Models.Task.LocalTask
        {
            Id = "net-err", Title = "Offline task", Status = "pending",
            Priority = "medium", IsSynced = false,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        _handler.RespondWithNetworkError();

        var ok = await _svc.DeleteTaskAsync("net-err");

        Assert.True(ok);
    }
}
