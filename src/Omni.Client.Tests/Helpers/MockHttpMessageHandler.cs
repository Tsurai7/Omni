using System.Net;
using System.Text;

namespace Omni.Client.Tests.Helpers;

/// <summary>
/// Configurable <see cref="HttpMessageHandler"/> for unit tests.
/// Register responses with <see cref="Respond"/> before sending requests.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private readonly List<HttpRequestMessage> _requests = new();

    /// <summary>All requests that have been sent through this handler.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    /// <summary>Enqueue a status-code-only response.</summary>
    public void Respond(HttpStatusCode status) =>
        Respond(_ => new HttpResponseMessage(status));

    /// <summary>Enqueue a JSON response.</summary>
    public void Respond(HttpStatusCode status, string json) =>
        Respond(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    /// <summary>Enqueue a response built by a factory for the next request.</summary>
    public void Respond(Func<HttpRequestMessage, HttpResponseMessage> factory) =>
        _responses.Enqueue(factory);

    /// <summary>Enqueue a network error (simulates server unreachable).</summary>
    public void RespondWithNetworkError(string message = "Connection refused") =>
        Respond(_ => throw new HttpRequestException(message));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _requests.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException(
                $"MockHttpMessageHandler: no response queued for {request.Method} {request.RequestUri}");
        var factory = _responses.Dequeue();
        return Task.FromResult(factory(request));
    }
}
