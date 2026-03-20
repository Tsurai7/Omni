using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Models.Auth;
using Omni.Client.Models.Chat;

namespace Omni.Client.Services;

public class ChatService : IChatService
{
    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly JsonSerializerOptions _json;

    public ChatService(HttpClient http, IAuthService auth, JsonSerializerOptions json)
    {
        _http = http;
        _auth = auth;
        _json = json;
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        var token = await _auth.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body != null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        return req;
    }

    public async Task<List<ConversationStarter>> GetStartersAsync(CancellationToken ct = default)
    {
        try
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return [];

            using var req = await BuildRequestAsync(HttpMethod.Get, $"api/ai/chat/{user.Id}/starters");
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];

            var result = await resp.Content.ReadFromJsonAsync<StartersResponse>(_json, ct);
            return result?.Starters ?? [];
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

            using var req = await BuildRequestAsync(HttpMethod.Get, $"api/ai/chat/{user.Id}/conversations");
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];

            var result = await resp.Content.ReadFromJsonAsync<ChatConversationsResponse>(_json, ct);
            return result?.Conversations ?? [];
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

            using var req = await BuildRequestAsync(HttpMethod.Get,
                $"api/ai/chat/{user.Id}/conversations/{conversationId}/messages?limit={limit}");
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];

            var result = await resp.Content.ReadFromJsonAsync<ChatMessagesResponse>(_json, ct);
            return result?.Messages ?? [];
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
        UserResponse? user = null;
        try
        {
            user = await _auth.GetCurrentUserAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatService.SendMessageAsync auth: {ex.Message}");
        }

        if (user == null) yield break;

        var body = new SendMessageRequest(conversationId, content);
        HttpRequestMessage? req = null;
        HttpResponseMessage? resp = null;

        try
        {
            req = await BuildRequestAsync(HttpMethod.Post, $"api/ai/chat/{user.Id}/messages", body);
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
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
                ? "Coach isn’t available: the gateway needs AI_URL pointing at the omni-ai service (e.g. http://ai:8000 in Docker)."
                : $"Couldn’t reach the coach ({(int)status}). Try again.";
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

            using var req = await BuildRequestAsync(HttpMethod.Delete,
                $"api/ai/chat/{user.Id}/conversations/{conversationId}");
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                Debug.WriteLine($"ChatService.DeleteConversationAsync: status {resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatService.DeleteConversationAsync: {ex.Message}");
        }
    }
}
