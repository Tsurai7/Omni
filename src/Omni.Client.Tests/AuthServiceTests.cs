using System.Net;
using Omni.Client.Services;
using Omni.Client.Tests.Fakes;
using Omni.Client.Tests.Helpers;
using Xunit;

namespace Omni.Client.Tests;

public sealed class AuthServiceTests
{
    // ── helpers ─────────────────────────────────────────────────────────

    private static (AuthService Service, MockHttpMessageHandler Handler, FakeTokenStorage Storage)
        Build()
    {
        var (client, handler) = TestHttpClientFactory.Create();
        var storage = new FakeTokenStorage();
        var svc = new AuthService(client, TestHttpClientFactory.JsonOptions, storage);
        return (svc, handler, storage);
    }

    // ── IsTokenExpired (static, internal) ───────────────────────────────

    [Fact]
    public void IsTokenExpired_ValidToken_ReturnsFalse()
    {
        var token = JwtHelper.CreateToken(DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(AuthService.IsTokenExpired(token));
    }

    [Fact]
    public void IsTokenExpired_ExpiredToken_ReturnsTrue()
    {
        var token = JwtHelper.CreateToken(DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.True(AuthService.IsTokenExpired(token));
    }

    [Fact]
    public void IsTokenExpired_MalformedToken_ReturnsTrue()
    {
        Assert.True(AuthService.IsTokenExpired("not.a.jwt.at.all"));
        Assert.True(AuthService.IsTokenExpired(""));
        Assert.True(AuthService.IsTokenExpired("only.two"));
    }

    [Fact]
    public void IsTokenExpired_TokenWithoutExpClaim_ReturnsFalse()
    {
        Assert.False(AuthService.IsTokenExpired(JwtHelper.TokenWithoutExp));
    }

    // ── GetTokenAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetTokenAsync_NoStoredToken_ReturnsNull()
    {
        var (svc, _, _) = Build();
        Assert.Null(await svc.GetTokenAsync());
    }

    [Fact]
    public async Task GetTokenAsync_ValidTokenInSecureStorage_ReturnsToken()
    {
        var (svc, _, storage) = Build();
        var token = JwtHelper.ValidToken;
        storage.SeedSecure("omni_jwt_token", token);

        var result = await svc.GetTokenAsync();

        Assert.Equal(token, result);
    }

    [Fact]
    public async Task GetTokenAsync_ExpiredTokenInStorage_ReturnsNull_AndClearsStorage()
    {
        var (svc, _, storage) = Build();
        storage.SeedSecure("omni_jwt_token", JwtHelper.ExpiredToken);

        var result = await svc.GetTokenAsync();

        Assert.Null(result);
        // Storage should be cleared after detecting expiry
        Assert.Null(await storage.GetSecureAsync("omni_jwt_token"));
    }

    [Fact]
    public async Task GetTokenAsync_ValidInMemoryToken_DoesNotHitStorage()
    {
        var (svc, _, storage) = Build();
        var token = JwtHelper.ValidToken;
        // Pre-set in-memory via a successful login path: seed secure and load
        storage.SeedSecure("omni_jwt_token", token);
        await svc.GetTokenAsync(); // warm in-memory cache

        // Clear storage to prove the second call uses in-memory
        storage.RemoveSecure("omni_jwt_token");
        var result = await svc.GetTokenAsync();

        Assert.Equal(token, result);
    }

    [Fact]
    public async Task GetTokenAsync_PreferencesFallbackUsed_WhenSecureStorageEmpty()
    {
        var (svc, _, storage) = Build();
        var token = JwtHelper.ValidToken;
        storage.SeedPreference("omni_jwt_token_prefs", token);

        var result = await svc.GetTokenAsync();

        Assert.Equal(token, result);
    }

    // ── IsAuthenticatedAsync ────────────────────────────────────────────

    [Fact]
    public async Task IsAuthenticatedAsync_WithValidToken_ReturnsTrue()
    {
        var (svc, _, storage) = Build();
        storage.SeedSecure("omni_jwt_token", JwtHelper.ValidToken);
        Assert.True(await svc.IsAuthenticatedAsync());
    }

    [Fact]
    public async Task IsAuthenticatedAsync_WithoutToken_ReturnsFalse()
    {
        var (svc, _, _) = Build();
        Assert.False(await svc.IsAuthenticatedAsync());
    }

    // ── LoginAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_Success_ReturnsTokenAndSetsInMemory()
    {
        var (svc, handler, _) = Build();
        var token = JwtHelper.ValidToken;
        handler.Respond(HttpStatusCode.OK,
            $$"""{"token":"{{token}}","expires_at":"2099-01-01T00:00:00Z"}""");

        var result = await svc.LoginAsync("user@test.com", "Password1!");

        Assert.NotNull(result);
        Assert.Equal(token, result.Token);
        // Should now be authenticated
        Assert.Equal(token, await svc.GetTokenAsync());
    }

    [Fact]
    public async Task LoginAsync_Unauthorized_ReturnsNullAndSetsLastAuthError()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.Unauthorized,
            """{"error":"Invalid credentials"}""");

        var result = await svc.LoginAsync("bad@test.com", "wrong");

        Assert.Null(result);
        Assert.Equal("Invalid credentials", svc.LastAuthError);
    }

    [Fact]
    public async Task LoginAsync_NetworkError_ReturnsNullAndSetsLastAuthError()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError("Connection refused");

        var result = await svc.LoginAsync("user@test.com", "pass");

        Assert.Null(result);
        Assert.NotNull(svc.LastAuthError);
    }

    [Fact]
    public async Task LoginAsync_TrimsEmailWhitespace()
    {
        var (svc, handler, _) = Build();
        var token = JwtHelper.ValidToken;
        handler.Respond(HttpStatusCode.OK,
            $$"""{"token":"{{token}}","expires_at":"2099-01-01T00:00:00Z"}""");

        await svc.LoginAsync("  user@test.com  ", "pass");

        // Verify trimmed email was sent (request body check)
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("user@test.com", body);
        Assert.DoesNotContain("  user@test.com  ", body);
    }

    // ── RegisterAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_Success_ReturnsRegistrationResult()
    {
        var (svc, handler, _) = Build();
        var token = JwtHelper.ValidToken;
        handler.Respond(HttpStatusCode.OK,
            $$"""{"id":"u1","email":"new@test.com","token":"{{token}}","expires_at":"2099-01-01T00:00:00Z"}""");

        var result = await svc.RegisterAsync("new@test.com", "Password1!");

        Assert.NotNull(result);
        Assert.Equal("new@test.com", result.Email);
        Assert.Equal(token, result.Token);
    }

    [Fact]
    public async Task RegisterAsync_Conflict_ReturnsNullWithError()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.Conflict,
            """{"error":"Email already in use"}""");

        var result = await svc.RegisterAsync("existing@test.com", "pass");

        Assert.Null(result);
        Assert.Equal("Email already in use", svc.LastAuthError);
    }

    [Fact]
    public async Task RegisterAsync_NetworkError_ReturnsNull()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var result = await svc.RegisterAsync("u@test.com", "pass");

        Assert.Null(result);
        Assert.NotNull(svc.LastAuthError);
    }

    // ── GetCurrentUserAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentUserAsync_NoToken_ReturnsNull()
    {
        var (svc, _, _) = Build();
        Assert.Null(await svc.GetCurrentUserAsync());
    }

    [Fact]
    public async Task GetCurrentUserAsync_Success_ReturnsAndCachesUser()
    {
        var (svc, handler, storage) = Build();
        storage.SeedSecure("omni_jwt_token", JwtHelper.ValidToken);
        handler.Respond(HttpStatusCode.OK, """{"id":"u1","email":"me@test.com"}""");

        var user1 = await svc.GetCurrentUserAsync();
        // Second call must NOT hit the network (cached)
        var user2 = await svc.GetCurrentUserAsync();

        Assert.Equal("me@test.com", user1?.Email);
        Assert.Same(user1, user2);
        Assert.Single(handler.Requests); // only one HTTP call
    }

    [Fact]
    public async Task GetCurrentUserAsync_ServerError_ReturnsNull()
    {
        var (svc, handler, storage) = Build();
        storage.SeedSecure("omni_jwt_token", JwtHelper.ValidToken);
        handler.Respond(HttpStatusCode.InternalServerError);

        Assert.Null(await svc.GetCurrentUserAsync());
    }

    [Fact]
    public async Task GetCurrentUserAsync_NetworkError_ReturnsNull()
    {
        var (svc, handler, storage) = Build();
        storage.SeedSecure("omni_jwt_token", JwtHelper.ValidToken);
        handler.RespondWithNetworkError();

        Assert.Null(await svc.GetCurrentUserAsync());
    }

    // ── Logout ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_ClearsInMemoryTokenAndStorage()
    {
        var (svc, handler, storage) = Build();
        var token = JwtHelper.ValidToken;
        storage.SeedSecure("omni_jwt_token", token);
        storage.SeedPreference("omni_jwt_token_prefs", token);
        handler.Respond(HttpStatusCode.OK, """{"token":"t","expires_at":"2099"}""");

        // Warm in-memory cache
        await svc.GetTokenAsync();

        svc.Logout();

        Assert.Null(await svc.GetTokenAsync()); // in-memory cleared
        Assert.Null(await storage.GetSecureAsync("omni_jwt_token")); // secure cleared
        Assert.Equal("", storage.GetPreference("omni_jwt_token_prefs")); // prefs cleared
    }
}
