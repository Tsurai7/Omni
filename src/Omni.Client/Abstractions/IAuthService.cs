using Omni.Client.Models.Auth;

namespace Omni.Client.Abstractions;

public interface IAuthService
{
    /// <summary>Server-side error message from the most recent Register or Login call. Null on success.</summary>
    string? LastAuthError { get; }

    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
    Task<UserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    Task<RegisterResponse?> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<TokenResponse?> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    void Logout();
}
