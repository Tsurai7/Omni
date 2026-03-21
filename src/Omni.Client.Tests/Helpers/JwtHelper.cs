using System.Text;
using System.Text.Json;

namespace Omni.Client.Tests.Helpers;

/// <summary>Generates minimal JWT tokens for tests (no signature verification).</summary>
public static class JwtHelper
{
    private static string Base64UrlEncode(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>Creates a JWT with an <c>exp</c> claim set to <paramref name="expiresAt"/>.</summary>
    public static string CreateToken(DateTimeOffset expiresAt)
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}""");
        var payload = Base64UrlEncode(
            JsonSerializer.Serialize(new { sub = "user-1", exp = expiresAt.ToUnixTimeSeconds() }));
        return $"{header}.{payload}.fakesignature";
    }

    /// <summary>A JWT that is already expired.</summary>
    public static string ExpiredToken =>
        CreateToken(DateTimeOffset.UtcNow.AddHours(-1));

    /// <summary>A JWT that is valid for one hour.</summary>
    public static string ValidToken =>
        CreateToken(DateTimeOffset.UtcNow.AddHours(1));

    /// <summary>A valid token with no <c>exp</c> claim — treated as always valid.</summary>
    public static string TokenWithoutExp
    {
        get
        {
            var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}""");
            var payload = Base64UrlEncode("""{"sub":"user-1"}""");
            return $"{header}.{payload}.fakesig";
        }
    }
}
