using System.Text.Json.Serialization;

namespace Omni.Client.Models.Usage;

public sealed class UsageSyncEntry
{
    [JsonPropertyName("app_name")]
    public string AppName { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("duration_seconds")]
    public long DurationSeconds { get; set; }
}

public sealed class UsageSyncRequest
{
    [JsonPropertyName("entries")]
    public List<UsageSyncEntry> Entries { get; set; } = new();
}

public sealed class UsageListEntry
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("app_name")]
    public string AppName { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("total_seconds")]
    public long TotalSeconds { get; set; }
}

public sealed class UsageListResponse
{
    [JsonPropertyName("entries")]
    public List<UsageListEntry> Entries { get; set; } = new();
}
