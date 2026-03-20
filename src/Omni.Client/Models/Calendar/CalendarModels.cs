using Microsoft.Maui.Graphics;

namespace Omni.Client.Models.Calendar;

public enum CalendarEventSource { OmniTask, GoogleCalendar }

/// <summary>A unified calendar event (task or Google Calendar event).</summary>
public record CalendarEvent(
    string Id,
    string Title,
    DateTime StartAt,
    DateTime? EndAt,
    bool IsAllDay,
    CalendarEventSource Source,
    string? Color,
    string? OmniTaskId,
    string? GoogleEventId,
    string? Priority,
    string? TaskStatus,
    string? Description)
{
    public bool IsOmniTask       => Source == CalendarEventSource.OmniTask;
    public bool IsGoogleCalendar => Source == CalendarEventSource.GoogleCalendar;

    public Color EventColor => Source switch
    {
        CalendarEventSource.GoogleCalendar => Color.FromArgb(Color ?? "#4A90E2"),
        _ => Priority?.ToLowerInvariant() switch
        {
            "high"   => Color.FromArgb("#FF5C5C"),
            "low"    => Color.FromArgb("#4ECCA3"),
            _        => Color.FromArgb("#F5A623"),
        },
    };

    public string TimeLabel
    {
        get
        {
            if (IsAllDay) return "All day";
            return EndAt.HasValue
                ? $"{StartAt:HH:mm} – {EndAt.Value:HH:mm}"
                : StartAt.ToString("HH:mm");
        }
    }

    public string SourceIcon => Source == CalendarEventSource.GoogleCalendar ? "🗓" : "✅";
}

/// <summary>Response from GET /api/calendar/events.</summary>
public record CalendarEventsResponse
{
    public List<CalendarEventDto> Events { get; init; } = new();
}

public record CalendarEventDto(
    string Id = "",
    string Title = "",
    string StartAt = "",
    string? EndAt = null,
    bool IsAllDay = false,
    string Source = "omni_task",
    string? Color = null,
    string? OmniTaskId = null,
    string? GoogleEventId = null,
    string? Priority = null,
    string? TaskStatus = null,
    string? Description = null)
{
    public CalendarEvent ToCalendarEvent()
    {
        DateTime.TryParse(StartAt, out var start);
        DateTime? end = EndAt != null && DateTime.TryParse(EndAt, out var e) ? e : null;
        var source = Source == "google_calendar"
            ? CalendarEventSource.GoogleCalendar
            : CalendarEventSource.OmniTask;
        return new CalendarEvent(Id, Title, start, end, IsAllDay, source,
            Color, OmniTaskId, GoogleEventId, Priority, TaskStatus, Description);
    }
}

/// <summary>Calendar connection status from GET /api/calendar/status.</summary>
public record CalendarStatus(
    bool Connected = false,
    string? Email = null,
    string? LastSyncedAt = null);

/// <summary>Google OAuth URL from GET /api/calendar/auth/google.</summary>
public record CalendarAuthUrl(string Url = "");
