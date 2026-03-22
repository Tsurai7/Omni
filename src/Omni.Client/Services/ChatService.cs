using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Core.Abstractions.Api;
using Omni.Client.Models.Chat;
using Refit;

namespace Omni.Client.Services;

public class ChatService : IChatService
{
    private readonly HttpClient _streamHttp;
    private readonly IAiApi _api;
    private readonly IAuthService _auth;
    private readonly JsonSerializerOptions _json;

    public ChatService(
        HttpClient streamHttp,
        IAiApi api,
        IAuthService auth,
        JsonSerializerOptions json)
    {
        _streamHttp = streamHttp;
        _api = api;
        _auth = auth;
        _json = json;
    }

    public async Task<List<ConversationStarter>> GetStartersAsync(CancellationToken ct = default)
    {
        try
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return [];

            var result = await _api.GetStartersAsync(user.Id, ct);
            return result?.Starters ?? [];
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"ChatService.GetStartersAsync: {ex.StatusCode}");
            return [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatService.GetStartersAsync: {ex.Message}");
            return [];
        }
    }

    public async Task<List<ChatConversation>> GetConversationsAsync(CancellationToken ct = default)
    {
        try
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return [];

            var result = await _api.GetConversationsAsync(user.Id, ct);
            return result?.Conversations ?? [];
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"ChatService.GetConversationsAsync: {ex.StatusCode}");
            return [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatService.GetConversationsAsync: {ex.Message}");
            return [];
        }
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(string conversationId, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return [];

            var result = await _api.GetMessagesAsync(user.Id, conversationId, limit, ct);
            return result?.Messages ?? [];
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"ChatService.GetMessagesAsync: {ex.StatusCode}");
            return [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatService.GetMessagesAsync: {ex.Message}");
            return [];
        }
    }

    public async IAsyncEnumerable<ChatStreamDelta> SendMessageAsync(
        string? conversationId,
        string content,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var user = await _auth.GetCurrentUserAsync();
        if (user == null) yield break;

        var token = await _auth.GetTokenAsync();

        var body = new SendMessageRequest(conversationId, content);
        HttpRequestMessage? req = null;
        HttpResponseMessage? resp = null;

        try
        {
            req = new HttpRequestMessage(HttpMethod.Post, $"api/ai/chat/{user.Id}/messages");
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
            resp = await _streamHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatService.SendMessageAsync request: {ex.Message}");
            req?.Dispose();
            resp?.Dispose();
            yield break;
        }

        if (!resp.IsSuccessStatusCode)
        {
            var status = resp.StatusCode;
            var errorBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Debug.WriteLine($"ChatService.SendMessageAsync: {(int)status} {errorBody}");
            resp.Dispose();
            req.Dispose();
            var msg = status == HttpStatusCode.ServiceUnavailable
                ? "Coach isn't available: the gateway needs AI_URL pointing at the omni-ai service (e.g. http://ai:8000 in Docker)."
                : $"Couldn't reach the coach ({(int)status}). Try again.";
            yield return new ChatStreamDelta(msg, null, false, true);
            yield return new ChatStreamDelta(null, null, true, null);
            yield break;
        }

        Stream? stream = null;
        StreamReader? reader = null;
        try
        {
            stream = await resp.Content.ReadAsStreamAsync(ct);
            reader = new StreamReader(stream);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var json = line[6..];
                ChatStreamDelta? delta = null;
                try
                {
                    delta = JsonSerializer.Deserialize<ChatStreamDelta>(json, _json);
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"ChatService SSE parse error: {ex.Message}");
                    continue;
                }

                if (delta != null)
                    yield return delta;

                if (delta?.Done == true)
                    break;
            }
        }
        finally
        {
            reader?.Dispose();
            stream?.Dispose();
            resp.Dispose();
            req.Dispose();
        }
    }

    public async Task DeleteConversationAsync(string conversationId, CancellationToken ct = default)
    {
        try
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return;

            await _api.DeleteConversationAsync(user.Id, conversationId, ct);
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"ChatService.DeleteConversationAsync: {ex.StatusCode}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatService.DeleteConversationAsync: {ex.Message}");
        }
    }
}
