namespace Omni.Client.Models.Usage;

public sealed class UsageSyncEntry
{
    public string AppName { get; set; } = "";
    public string Category { get; set; } = "";
    public long DurationSeconds { get; set; }
}

public sealed class UsageSyncRequest
{
    public List<UsageSyncEntry> Entries { get; set; } = new();
}

public sealed class UsageListEntry
{
    public string Date { get; set; } = "";
    public string AppName { get; set; } = "";
    public string Category { get; set; } = "";
    public long TotalSeconds { get; set; }
}

public sealed class UsageListResponse
{
    public List<UsageListEntry> Entries { get; set; } = new();
}
