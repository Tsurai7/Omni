namespace Omni.Client.Models.Auth;

public record LoginRequest(string Email = "", string Password = "");

public record RegisterRequest(string Email = "", string Password = "");

public record TokenResponse(string Token = "", string ExpiresAt = "");

public record RegisterResponse(string Id = "", string Email = "", string Token = "", string ExpiresAt = "");

public record UserResponse(string Id = "", string Email = "");
