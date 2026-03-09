using System.Collections.ObjectModel;

namespace Omni.Client.Models.Session;

public record SessionSyncEntry(
    string Name = "",
    string ActivityType = "other",
    string StartedAt = "",
    long DurationSeconds = 0,
    /// <summary>Optional. Links session to a daily goal for backend analytics.</summary>
    string? GoalId = null,
    /// <summary>Optional. Target minutes for this session (e.g. 25, 60).</summary>
    int? GoalTargetMinutes = null,
    /// <summary>Optional. Concentration score 0–100 from distraction tracking.</summary>
    int? SessionScore = null,
    /// <summary>Optional. Number of distraction events during the session.</summary>
    int? DistractionEventCount = null);

public record SessionSyncRequest
{
    public List<SessionSyncEntry> Entries { get; init; } = new();
}

public record SessionListEntry(
    string Id = "",
    string Name = "",
    string ActivityType = "",
    string StartedAt = "",
    long DurationSeconds = 0,
    /// <summary>Optional. Concentration score 0–100 when provided by backend.</summary>
    int? SessionScore = null,
    /// <summary>Optional. Distraction event count when provided by backend.</summary>
    int? DistractionEventCount = null);

public record SessionListResponse
{
    public List<SessionListEntry> Entries { get; init; } = new();
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
