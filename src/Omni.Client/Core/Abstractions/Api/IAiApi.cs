using Omni.Client.Models.Chat;
using Omni.Client.Models.FocusScore;
using Refit;

namespace Omni.Client.Core.Abstractions.Api;

public interface IAiApi
{
    [Get("/api/ai/focus-score/{userId}")]
    Task<FocusScoreResponse> GetFocusScoreAsync(string userId, CancellationToken ct = default);

    [Get("/api/ai/chat/{userId}/starters")]
    Task<StartersResponse> GetStartersAsync(string userId, CancellationToken ct = default);

    [Get("/api/ai/chat/{userId}/conversations")]
    Task<ChatConversationsResponse> GetConversationsAsync(string userId, CancellationToken ct = default);

    [Get("/api/ai/chat/{userId}/conversations/{conversationId}/messages")]
    Task<ChatMessagesResponse> GetMessagesAsync(
        string userId,
        string conversationId,
        [AliasAs("limit")] int limit = 20,
        CancellationToken ct = default);

    [Delete("/api/ai/chat/{userId}/conversations/{conversationId}")]
    Task DeleteConversationAsync(string userId, string conversationId, CancellationToken ct = default);
}
