using System.Net.Http.Headers;
using Omni.Client.Abstractions;

namespace Omni.Client.Infrastructure.Http;

public sealed class AuthenticatedHttpHandler : DelegatingHandler
{
    private readonly ITokenStorage _tokenStorage;
    private const string TokenKey = "omni_jwt_token";
    private const string TokenKeyPreferences = "omni_jwt_token_prefs";

    public AuthenticatedHttpHandler(ITokenStorage tokenStorage)
    {
        _tokenStorage = tokenStorage;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenStorage.GetSecureAsync(TokenKey);
        if (string.IsNullOrEmpty(token))
            token = _tokenStorage.GetPreference(TokenKeyPreferences);

        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
