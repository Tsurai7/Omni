namespace Omni.Client.Models.Usage;

public record UsageSyncEntry(string AppName = "", string Category = "", long DurationSeconds = 0);

public record UsageSyncRequest(List<UsageSyncEntry> Entries);

public record UsageListEntry(string Date = "", string AppName = "", string Category = "", long TotalSeconds = 0);

public record UsageListResponse(List<UsageListEntry> Entries);
