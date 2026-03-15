namespace Omni.Client.Models.Productivity;

public record NotificationItem(
    string Id,
    DateTime? CreatedAt,
    string Type,
    string? Title,
    string? Body,
    string? ActionType,
    System.Text.Json.JsonElement? ActionPayload,
    DateTime? ReadAt
);

public record NotificationsResponse(List<NotificationItem> Items);
