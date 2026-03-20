using System.IO;
using System.Linq;
using SQLite;
using Omni.Client.Models.Task;

namespace Omni.Client.Services;

/// <summary>Owns the local SQLite database: path, connection, and migrations.</summary>
public sealed class LocalDatabaseService
{
    private readonly Lazy<SQLiteAsyncConnection> _connection;
    private const string FileName = "omni_local.db";

    public LocalDatabaseService()
    {
        _connection = new Lazy<SQLiteAsyncConnection>(() =>
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, FileName);
            return new SQLiteAsyncConnection(path);
        });
    }

    public SQLiteAsyncConnection Connection => _connection.Value;

    /// <summary>Creates or updates schema. Call once at startup.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Connection.CreateTableAsync<LocalTask>().ConfigureAwait(false);
        await Connection.CreateTableAsync<PendingSyncRow>().ConfigureAwait(false);
    }

    /// <summary>Enqueue a payload for later sync. Kind is "usage" or "session".</summary>
    public async Task SavePendingSyncAsync(string kind, string payload, CancellationToken cancellationToken = default)
    {
        var row = new PendingSyncRow
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = kind,
            Payload = payload,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow
        };
        await Connection.InsertAsync(row).ConfigureAwait(false);
    }

    /// <summary>Get all unsynced pending rows for drain.</summary>
    public async Task<List<PendingSyncRow>> GetUnsyncedPendingAsync(CancellationToken cancellationToken = default)
    {
        return await Connection.Table<PendingSyncRow>()
            .Where(r => !r.IsSynced)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    /// <summary>Mark a pending row as synced after successful API response.</summary>
    public async Task MarkPendingSyncedAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = await Connection.Table<PendingSyncRow>().Where(r => r.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);
        if (row != null)
        {
            row.IsSynced = true;
            await Connection.UpdateAsync(row).ConfigureAwait(false);
        }
    }

    /// <summary>Insert a task for local storage and later sync.</summary>
    public async Task InsertTaskAsync(LocalTask task, CancellationToken cancellationToken = default)
    {
        await Connection.InsertAsync(task).ConfigureAwait(false);
    }

    /// <summary>Get all tasks (synced and unsynced) for display. Optional filter by status.</summary>
    public async Task<List<LocalTask>> GetAllTasksAsync(CancellationToken cancellationToken = default)
    {
        return await Connection.Table<LocalTask>()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    /// <summary>Get all unsynced tasks for drain.</summary>
    public async Task<List<LocalTask>> GetUnsyncedTasksAsync(CancellationToken cancellationToken = default)
    {
        return await Connection.Table<LocalTask>()
            .Where(t => !t.IsSynced)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    /// <summary>Mark a task as synced and optionally set ServerId.</summary>
    public async Task MarkTaskSyncedAsync(string localId, string? serverId, CancellationToken cancellationToken = default)
    {
        var task = await Connection.Table<LocalTask>().Where(t => t.Id == localId).FirstOrDefaultAsync().ConfigureAwait(false);
        if (task != null)
        {
            task.IsSynced = true;
            if (serverId != null)
                task.ServerId = serverId;
            task.UpdatedAt = DateTime.UtcNow;
            await Connection.UpdateAsync(task).ConfigureAwait(false);
        }
    }

    /// <summary>Update task status locally and mark for sync (IsSynced = false). Finds by Id or ServerId.</summary>
    public async Task UpdateTaskStatusAsync(string? localId, string? serverId, string status, CancellationToken cancellationToken = default)
    {
        List<LocalTask> candidates = new();
        if (!string.IsNullOrEmpty(localId))
        {
            var byLocal = await Connection.Table<LocalTask>().Where(t => t.Id == localId).FirstOrDefaultAsync().ConfigureAwait(false);
            if (byLocal != null) candidates.Add(byLocal);
        }
        if (!string.IsNullOrEmpty(serverId))
        {
            var byServer = await Connection.Table<LocalTask>().Where(t => t.ServerId == serverId).FirstOrDefaultAsync().ConfigureAwait(false);
            if (byServer != null && !candidates.Any(c => c.Id == byServer.Id))
                candidates.Add(byServer);
        }
        foreach (var task in candidates)
        {
            task.Status = status;
            task.UpdatedAt = DateTime.UtcNow;
            task.IsSynced = false;
            await Connection.UpdateAsync(task).ConfigureAwait(false);
        }
    }

    /// <summary>Update task title and priority locally and mark for sync.</summary>
    public async Task UpdateTaskAsync(string localId, string title, string priority, CancellationToken cancellationToken = default)
    {
        var task = await Connection.Table<LocalTask>().Where(t => t.Id == localId).FirstOrDefaultAsync().ConfigureAwait(false);
        if (task != null)
        {
            task.Title = title;
            task.Priority = priority;
            task.UpdatedAt = DateTime.UtcNow;
            task.IsSynced = false;
            await Connection.UpdateAsync(task).ConfigureAwait(false);
        }
    }

    /// <summary>Delete a task by local id (e.g. when deleting an unsynced task).</summary>
    public async Task DeleteTaskAsync(string localId, CancellationToken cancellationToken = default)
    {
        await Connection.Table<LocalTask>().DeleteAsync(t => t.Id == localId).ConfigureAwait(false);
    }
}
