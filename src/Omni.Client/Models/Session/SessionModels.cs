using System.Collections.ObjectModel;

namespace Omni.Client.Models.Session;

public sealed class SessionSyncEntry
{
    public string Name { get; set; } = "";
    public string ActivityType { get; set; } = "other";
    public string StartedAt { get; set; } = "";
    public long DurationSeconds { get; set; }
}

public sealed class SessionSyncRequest
{
    public List<SessionSyncEntry> Entries { get; set; } = new();
}

public sealed class SessionListEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ActivityType { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public long DurationSeconds { get; set; }
}

public sealed class SessionListResponse
{
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
