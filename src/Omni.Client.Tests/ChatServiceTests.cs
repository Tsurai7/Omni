using System.Net;
using System.Text;
using Omni.Client.Services;
using Omni.Client.Tests.Fakes;
using Omni.Client.Tests.Helpers;
using Xunit;

namespace Omni.Client.Tests;

public sealed class ChatServiceTests
{
    private static (ChatService Service, MockHttpMessageHandler Handler, FakeAuthService Auth)
        Build()
    {
        var (client, handler) = TestHttpClientFactory.Create();
        var auth = new FakeAuthService
        {
            Token = JwtHelper.ValidToken,
            User  = new Omni.Client.Models.Auth.UserResponse("user-1", "test@example.com")
        };
        var svc = new ChatService(client, auth, TestHttpClientFactory.JsonOptions);
        return (svc, handler, auth);
    }

    // ── GetStartersAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetStarters_NoUser_ReturnsEmpty()
    {
        var (svc, _, auth) = Build();
        auth.User = null;

        var result = await svc.GetStartersAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStarters_Success_ReturnsStarters()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK,
            """{"starters":[{"text":"How are you?","icon":"👋"},{"text":"What should I focus on?","icon":"🎯"}]}""");

        var result = await svc.GetStartersAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("How are you?", result[0].Text);
        Assert.Equal("🎯", result[1].Icon);
    }

    [Fact]
    public async Task GetStarters_ServerError_ReturnsEmpty()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.InternalServerError);

        var result = await svc.GetStartersAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStarters_NetworkError_ReturnsEmpty()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var result = await svc.GetStartersAsync();

        Assert.Empty(result);
    }

    // ── GetConversationsAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetConversations_Success_ReturnsList()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK,
            """{"conversations":[{"id":"c1","title":"Session recap","created_at":"2024-01-01","last_message_at":"2024-01-02"}]}""");

        var result = await svc.GetConversationsAsync();

        Assert.Single(result);
        Assert.Equal("c1", result[0].Id);
        Assert.Equal("Session recap", result[0].Title);
    }

    [Fact]
    public async Task GetConversations_NoUser_ReturnsEmpty()
    {
        var (svc, _, auth) = Build();
        auth.User = null;

        var result = await svc.GetConversationsAsync();

        Assert.Empty(result);
    }

    // ── GetMessagesAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetMessages_Success_ReturnsMessages()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK,
            "{\"messages\":[{\"id\":\"m1\",\"role\":\"user\",\"content\":\"Hello\",\"created_at\":\"2024-01-01\",\"metadata\":null}," +
            "{\"id\":\"m2\",\"role\":\"assistant\",\"content\":\"Hi there!\",\"created_at\":\"2024-01-01\",\"metadata\":null}]}");

        var result = await svc.GetMessagesAsync("conv-1");

        Assert.Equal(2, result.Count);
        Assert.Equal("user", result[0].Role);
        Assert.Equal("Hi there!", result[1].Content);
    }

    [Fact]
    public async Task GetMessages_NoUser_ReturnsEmpty()
    {
        var (svc, _, auth) = Build();
        auth.User = null;

        var result = await svc.GetMessagesAsync("conv-1");

        Assert.Empty(result);
    }

    // ── SendMessageAsync (SSE streaming) ────────────────────────────────

    [Fact]
    public async Task SendMessage_NoUser_YieldsNothing()
    {
        var (svc, _, auth) = Build();
        auth.User = null;

        var deltas = new List<Omni.Client.Models.Chat.ChatStreamDelta>();
        await foreach (var d in svc.SendMessageAsync(null, "Hello"))
            deltas.Add(d);

        Assert.Empty(deltas);
    }

    [Fact]
    public async Task SendMessage_SSEStream_YieldsDeltas()
    {
        var (svc, handler, _) = Build();
        // Build a minimal SSE response with two data lines + done
        var sse = BuildSse(
            """{"delta":"Hello ","conversation_id":"c1","done":false}""",
            """{"delta":"world!","conversation_id":"c1","done":false}""",
            """{"delta":null,"conversation_id":"c1","done":true}""");

        handler.Respond(HttpStatusCode.OK, sse);

        var deltas = new List<Omni.Client.Models.Chat.ChatStreamDelta>();
        await foreach (var d in svc.SendMessageAsync(null, "Hi"))
            deltas.Add(d);

        Assert.Equal(3, deltas.Count);
        Assert.Equal("Hello ", deltas[0].Delta);
        Assert.Equal("world!", deltas[1].Delta);
        Assert.True(deltas[2].Done);
    }

    [Fact]
    public async Task SendMessage_503ServiceUnavailable_YieldsErrorDelta()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.ServiceUnavailable, "gateway not configured");

        var deltas = new List<Omni.Client.Models.Chat.ChatStreamDelta>();
        await foreach (var d in svc.SendMessageAsync(null, "hi"))
            deltas.Add(d);

        Assert.True(deltas.Any(d => d.Error == true));
        Assert.True(deltas.Any(d => d.Delta?.Contains("gateway") == true));
    }

    [Fact]
    public async Task SendMessage_OtherHttpError_YieldsGenericErrorDelta()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.BadGateway, "upstream error");

        var deltas = new List<Omni.Client.Models.Chat.ChatStreamDelta>();
        await foreach (var d in svc.SendMessageAsync(null, "hi"))
            deltas.Add(d);

        Assert.True(deltas.Any(d => d.Error == true));
    }

    [Fact]
    public async Task SendMessage_MalformedSseLine_SkipsAndContinues()
    {
        var (svc, handler, _) = Build();
        // Mix a bad JSON line with a valid done marker
        var sse = "data: {invalid json\n\n" +
                  """data: {"delta":null,"conversation_id":"c1","done":true}""" + "\n\n";
        handler.Respond(HttpStatusCode.OK, sse);

        var deltas = new List<Omni.Client.Models.Chat.ChatStreamDelta>();
        await foreach (var d in svc.SendMessageAsync(null, "hi"))
            deltas.Add(d);

        // Bad line skipped; done delta received
        Assert.Single(deltas);
        Assert.True(deltas[0].Done);
    }

    // ── DeleteConversationAsync ─────────────────────────────────────────

    [Fact]
    public async Task DeleteConversation_Success_DoesNotThrow()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.NoContent);

        await svc.DeleteConversationAsync("conv-1"); // should not throw
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
    }

    [Fact]
    public async Task DeleteConversation_NetworkError_DoesNotThrow()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var ex = await Record.ExceptionAsync(() => svc.DeleteConversationAsync("conv-1"));
        Assert.Null(ex); // must not propagate
    }

    [Fact]
    public async Task DeleteConversation_NoUser_DoesNotSendRequest()
    {
        var (svc, handler, auth) = Build();
        auth.User = null;

        await svc.DeleteConversationAsync("conv-1");

        Assert.Empty(handler.Requests);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static string BuildSse(params string[] payloads)
    {
        var sb = new StringBuilder();
        foreach (var payload in payloads)
            sb.Append("data: ").Append(payload).Append("\n\n");
        return sb.ToString();
    }

    private static void Respond(MockHttpMessageHandler handler, HttpStatusCode status, string body)
    {
        // Helper that creates a response with text/event-stream content type
        handler.Respond(_ => new System.Net.Http.HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
        });
    }
}
