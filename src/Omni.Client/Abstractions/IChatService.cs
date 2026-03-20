using Omni.Client.Models.Chat;

namespace Omni.Client.Abstractions;

public interface IChatService
{
    /// <summary>Get context-aware conversation starters for the current user.</summary>
    Task<List<ConversationStarter>> GetStartersAsync(CancellationToken ct = default);

    /// <summary>List the current user's recent conversations.</summary>
    Task<List<ChatConversation>> GetConversationsAsync(CancellationToken ct = default);

    /// <summary>Get message history for a conversation.</summary>
    Task<List<ChatMessage>> GetMessagesAsync(string conversationId, int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Send a user message and stream the AI response as ChatStreamDeltas.
    /// Pass null conversationId to start a new conversation.
    /// </summary>
    IAsyncEnumerable<ChatStreamDelta> SendMessageAsync(string? conversationId, string content, CancellationToken ct = default);

    /// <summary>Soft-delete a conversation.</summary>
    Task DeleteConversationAsync(string conversationId, CancellationToken ct = default);
}
