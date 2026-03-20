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
    private readonly JsonSerializerOptions _jsonOptions;
    private string? _inMemoryToken;
    private UserResponse? _cachedUser;

    /// <inheritdoc/>
    public string? LastAuthError { get; private set; }

    public AuthService(HttpClient http, JsonSerializerOptions jsonOptions)
    {
        _http = http;
        _jsonOptions = jsonOptions;
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        return !string.IsNullOrEmpty(token);
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_inMemoryToken))
        {
            if (IsTokenExpired(_inMemoryToken))
            {
                Logout();
                return null;
            }
            return _inMemoryToken;
        }

        // Prefer SecureStorage; fallback to Preferences if Keychain isn't available (e.g. Mac Catalyst without entitlements)
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrEmpty(token))
            token = Preferences.Default.Get(TokenKeyPreferences, "");

        if (!string.IsNullOrEmpty(token))
        {
            if (IsTokenExpired(token))
            {
                // Clear the persisted expired token so the user is redirected to LoginPage on next start
                Logout();
                return null;
            }
            _inMemoryToken = token;
        }

        return string.IsNullOrEmpty(token) ? null : token;
    }

    /// <summary>Decodes the JWT payload and returns true if the token is expired or malformed.</summary>
    private static bool IsTokenExpired(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return true;

            var payload = parts[1];
            // Base64Url → Base64: replace URL-safe chars then restore padding
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expEl))
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expEl.GetInt64();

            return false; // No exp claim — treat as valid
        }
        catch
        {
            return true; // Malformed token → treat as expired
        }
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
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(_jsonOptions, cancellationToken);
        if (user != null)
            _cachedUser = user;
        return user;
    }

    public async Task<RegisterResponse?> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        LastAuthError = null;
        var request = new RegisterRequest(email.Trim(), password);
        using var response = await _http.PostAsJsonAsync("api/auth/register", request, _jsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LastAuthError = await ReadErrorAsync(response, cancellationToken);
            return null;
        }
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>(_jsonOptions, cancellationToken);
        if (body?.Token != null)
        {
            _inMemoryToken = body.Token;
            // Persist in background so SecureStorage (e.g. Keychain on Mac Catalyst) does not block the UI
            _ = PersistTokenAsync(body.Token);
        }
        return body;
    }

    public async Task<TokenResponse?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        LastAuthError = null;
        var request = new LoginRequest(email.Trim(), password);
        using var response = await _http.PostAsJsonAsync("api/auth/login", request, _jsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LastAuthError = await ReadErrorAsync(response, cancellationToken);
            return null;
        }
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(_jsonOptions, cancellationToken);
        if (body?.Token != null)
        {
            _inMemoryToken = body.Token;
            // Persist in background so SecureStorage (e.g. Keychain on Mac Catalyst) does not block the UI
            _ = PersistTokenAsync(body.Token);
        }
        return body;
    }

    /// <summary>Reads the server's JSON <c>{"error": "..."}</c> body, falling back to the HTTP status phrase.</summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString() ?? response.ReasonPhrase ?? response.StatusCode.ToString();
        }
        catch { /* ignore parse errors */ }
        return response.ReasonPhrase ?? response.StatusCode.ToString();
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
