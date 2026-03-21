using System.Text.Json;

namespace Omni.Client.Tests.Helpers;

/// <summary>Convenience factory: returns an <see cref="HttpClient"/> wired to a <see cref="MockHttpMessageHandler"/>.</summary>
public static class TestHttpClientFactory
{
    public static (HttpClient Client, MockHttpMessageHandler Handler) Create(
        string baseAddress = "http://localhost:8080/")
    {
        var handler = new MockHttpMessageHandler();
        var client  = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
        return (client, handler);
    }

    /// <summary>Default <see cref="JsonSerializerOptions"/> matching the app configuration (snake_case).</summary>
    public static JsonSerializerOptions JsonOptions => new()
    {
        PropertyNamingPolicy       = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
}
