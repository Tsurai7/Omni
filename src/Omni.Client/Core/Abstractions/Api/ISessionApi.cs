using Omni.Client.Models.Session;
using Refit;

namespace Omni.Client.Core.Abstractions.Api;

public interface ISessionApi
{
    [Post("/api/sessions/sync")]
    Task<SessionSyncOkResponse> SyncSessionsAsync([Body] SessionSyncRequest request, CancellationToken ct = default);

    [Get("/api/sessions")]
    Task<SessionListResponse> GetSessionsAsync(
        [AliasAs("from")] string? from = null,
        [AliasAs("to")] string? to = null,
        CancellationToken ct = default);
}

public record SessionSyncOkResponse(bool Ok = true);
