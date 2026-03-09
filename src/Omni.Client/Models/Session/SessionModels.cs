using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Omni.Client.Models.Session;

public sealed class SessionSyncEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("activity_type")]
    public string ActivityType { get; set; } = "other";

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("duration_seconds")]
    public long DurationSeconds { get; set; }
}

public sealed class SessionSyncRequest
{
    [JsonPropertyName("entries")]
    public List<SessionSyncEntry> Entries { get; set; } = new();
}

public sealed class SessionListEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("activity_type")]
    public string ActivityType { get; set; } = "";

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("duration_seconds")]
    public long DurationSeconds { get; set; }
}

public sealed class SessionListResponse
{
    [JsonPropertyName("entries")]
    public List<SessionListEntry> Entries { get; set; } = new();
}

public sealed class SessionDateGroup : ObservableCollection<SessionDisplayItem>
{
    public string Date { get; }

    public SessionDateGroup(string date, IEnumerable<SessionDisplayItem> items) : base(items)
    {
        Date = date;
    }
}

public sealed class SessionDisplayItem
{
    public string Name { get; set; } = "";
    public string ActivityType { get; set; } = "";
    public string StartedAtDisplay { get; set; } = "";
    public string DurationDisplay { get; set; } = "";

    public static SessionDisplayItem FromEntry(SessionListEntry e)
    {
        var startedAt = DateTime.TryParse(e.StartedAt, out var dt) ? dt : (DateTime?)null;
        var duration = TimeSpan.FromSeconds(e.DurationSeconds);
        return new SessionDisplayItem
        {
            Name = e.Name ?? "",
            ActivityType = e.ActivityType ?? "other",
            StartedAtDisplay = startedAt?.ToString("HH:mm") ?? "",
            DurationDisplay = duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
                : $"{duration.Minutes:D2}:{duration.Seconds:D2}"
        };
    }
}
