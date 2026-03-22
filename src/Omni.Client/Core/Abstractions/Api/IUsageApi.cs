using Omni.Client.Models.Usage;
using Refit;

namespace Omni.Client.Core.Abstractions.Api;

public interface IUsageApi
{
    [Post("/api/usage/sync")]
    Task<UsageSyncOkResponse> SyncAsync([Body] UsageSyncRequest request, CancellationToken ct = default);

    [Get("/api/usage")]
    Task<UsageListResponse> GetUsageAsync(
        [AliasAs("from")] string? from = null,
        [AliasAs("to")] string? to = null,
        [AliasAs("group_by")] string? groupBy = null,
        [AliasAs("category")] string? category = null,
        [AliasAs("app_name")] string? appName = null,
        CancellationToken ct = default);
}

public record UsageSyncOkResponse(bool Ok = true);
