using Omni.Client.Models.Calendar;
using Refit;

namespace Omni.Client.Core.Abstractions.Api;

public interface ICalendarApi
{
    [Get("/api/calendar/auth/google")]
    Task<CalendarAuthUrl> GetAuthUrlAsync(CancellationToken ct = default);

    [Post("/api/calendar/auth/google/connect")]
    Task ConnectAsync([Body] object request, CancellationToken ct = default);

    [Delete("/api/calendar/auth/google")]
    Task DisconnectAsync(CancellationToken ct = default);

    [Get("/api/calendar/status")]
    Task<CalendarStatus> GetStatusAsync(CancellationToken ct = default);

    [Get("/api/calendar/events")]
    Task<CalendarEventsResponse> GetEventsAsync(
        [AliasAs("start")] string start,
        [AliasAs("end")] string end,
        CancellationToken ct = default);

    [Post("/api/calendar/events")]
    Task CreateGoogleEventAsync([Body] object request, CancellationToken ct = default);

    [Post("/api/calendar/sync")]
    Task<CalendarSyncOkResponse> SyncAsync(CancellationToken ct = default);
}

public record CalendarSyncOkResponse(bool Synced = true);
