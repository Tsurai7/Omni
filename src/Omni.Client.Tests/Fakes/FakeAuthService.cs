using Omni.Client.Abstractions;
using Omni.Client.Models.Auth;

namespace Omni.Client.Tests.Fakes;

/// <summary>Controllable <see cref="IAuthService"/> for testing services that depend on auth.</summary>
public sealed class FakeAuthService : IAuthService
{
    public string? Token { get; set; }
    public UserResponse? User { get; set; }
    public string? LastAuthError { get; set; }
    public bool LoggedOut { get; private set; }

    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrEmpty(Token));

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Token);

    public Task<UserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(User);

    public Task<TokenResponse?> LoginAsync(string email, string password, CancellationToken cancellationToken = default) =>
        Task.FromResult<TokenResponse?>(null);

    public Task<RegisterResponse?> RegisterAsync(string email, string password, CancellationToken cancellationToken = default) =>
        Task.FromResult<RegisterResponse?>(null);

    public void Logout() => LoggedOut = true;
}
