using Omni.Client.Models.Auth;

namespace Omni.Client.Abstractions;

public interface IAuthService
{
    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
    Task<UserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    Task<RegisterResponse?> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<TokenResponse?> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    void Logout();
}
