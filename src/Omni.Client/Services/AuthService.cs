using System.Net.Http.Json;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Models.Auth;

namespace Omni.Client.Services;

public sealed class AuthService : IAuthService
{
    private const string TokenKey = "omni_jwt_token";
    private const string TokenKeyPreferences = "omni_jwt_token_prefs"; // fallback when SecureStorage fails (e.g. Mac Catalyst without Keychain)
    private readonly HttpClient _http;
    private string? _inMemoryToken;
    private UserResponse? _cachedUser;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AuthService(HttpClient http)
    {
        _http = http;
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        return !string.IsNullOrEmpty(token);
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_inMemoryToken))
            return _inMemoryToken;

        // Prefer SecureStorage; fallback to Preferences if Keychain isn't available (e.g. Mac Catalyst without entitlements)
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrEmpty(token))
            token = Preferences.Default.Get(TokenKeyPreferences, "");

        if (!string.IsNullOrEmpty(token))
            _inMemoryToken = token;

        return token;
    }

    public async Task<UserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
            return null;
        if (_cachedUser != null)
            return _cachedUser;
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        request.Headers.Add("Authorization", "Bearer " + token);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(JsonOptions, cancellationToken);
        if (user != null)
            _cachedUser = user;
        return user;
    }

    public async Task<RegisterResponse?> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var request = new RegisterRequest { Email = email.Trim(), Password = password };
        using var response = await _http.PostAsJsonAsync("api/auth/register", request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>(JsonOptions, cancellationToken);
        if (body?.Token != null)
        {
            _inMemoryToken = body.Token;
            await PersistTokenAsync(body.Token);
        }
        return body;
    }

    public async Task<TokenResponse?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var request = new LoginRequest { Email = email.Trim(), Password = password };
        using var response = await _http.PostAsJsonAsync("api/auth/login", request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);
        if (body?.Token != null)
        {
            _inMemoryToken = body.Token;
            await PersistTokenAsync(body.Token);
        }
        return body;
    }

    public void Logout()
    {
        _inMemoryToken = null;
        _cachedUser = null;
        SecureStorage.Default.Remove(TokenKey);
        Preferences.Default.Remove(TokenKeyPreferences);
    }

    private static async Task PersistTokenAsync(string token)
    {
        try
        {
            await SecureStorage.Default.SetAsync(TokenKey, token);
        }
        catch
        {
            // e.g. Mac Catalyst without Keychain entitlements
        }
        Preferences.Default.Set(TokenKeyPreferences, token);
    }
}
