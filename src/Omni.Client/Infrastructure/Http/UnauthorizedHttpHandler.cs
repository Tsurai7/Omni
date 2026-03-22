using System.Net;
using Omni.Client.Abstractions;

namespace Omni.Client.Infrastructure.Http;

public sealed class UnauthorizedHttpHandler : DelegatingHandler
{
    private readonly IAuthService _authService;

    public UnauthorizedHttpHandler(IAuthService authService)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            _authService.Logout();
        return response;
    }
}
