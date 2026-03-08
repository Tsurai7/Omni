using Omni.Client.Models.Usage;

namespace Omni.Client.Abstractions;

public interface IUsageService
{
    Task<bool> SyncAsync(CancellationToken cancellationToken = default);
    Task<UsageListResponse?> GetUsageAsync(string? from = null, string? to = null, string? groupBy = null, string? category = null, string? appName = null, CancellationToken cancellationToken = default);
    void StartPeriodicSync();
    void StopPeriodicSync();
}
