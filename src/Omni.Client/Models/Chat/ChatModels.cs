using System.Text.Json.Serialization;

namespace Omni.Client.Models.Chat;

public record ChatConversation(
    string Id,
    string Title,
    string? CreatedAt,
    string? LastMessageAt);

public record ChatConversationsResponse(List<ChatConversation> Conversations);

public record ChatMessageMetadata(
    List<ChatAction>? Actions,
    bool? MoodCheckin);

public record ChatAction(string Type, string Label, string? Title);

public record ChatMessage(
    string Id,
    string Role,
    string Content,
    string? CreatedAt,
    ChatMessageMetadata? Metadata);

public record ChatMessagesResponse(List<ChatMessage> Messages);

public record ConversationStarter(string Text, string Icon);

public record StartersResponse(List<ConversationStarter> Starters);

public record SendMessageRequest(
    [property: JsonPropertyName("conversation_id")] string? ConversationId,
    [property: JsonPropertyName("content")] string Content);

public record ChatStreamDelta(
    string? Delta,
    string? ConversationId,
    bool? Done,
    bool? Error,
    [property: JsonPropertyName("actions")] List<ChatAction>? Actions);
