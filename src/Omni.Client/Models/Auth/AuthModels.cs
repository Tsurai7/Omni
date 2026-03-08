namespace Omni.Client.Models.Auth;

public sealed class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class RegisterRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class TokenResponse
{
    public string Token { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
}

public sealed class RegisterResponse
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string Token { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
}

public sealed class UserResponse
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
}
