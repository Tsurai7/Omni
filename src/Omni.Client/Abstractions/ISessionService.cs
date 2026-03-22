using Omni.Client.Models.Session;

namespace Omni.Client.Abstractions;

public interface ISessionService
{
    Task<bool> SyncSessionsAsync(IReadOnlyList<SessionSyncEntry> entries, CancellationToken cancellationToken = default);
    Task<SessionListResponse?> GetSessionsAsync(string? from = null, string? to = null, int utcOffsetMinutes = 0, CancellationToken cancellationToken = default);
}
