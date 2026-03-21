using Omni.Client.Abstractions;

namespace Omni.Client.Tests.Fakes;

/// <summary>In-memory <see cref="ITokenStorage"/> for unit tests.</summary>
public sealed class FakeTokenStorage : ITokenStorage
{
    private readonly Dictionary<string, string> _secure = new();
    private readonly Dictionary<string, string> _prefs  = new();

    public Task<string?> GetSecureAsync(string key) =>
        Task.FromResult(_secure.TryGetValue(key, out var v) ? v : null);

    public Task SetSecureAsync(string key, string value)
    {
        _secure[key] = value;
        return Task.CompletedTask;
    }

    public void RemoveSecure(string key) => _secure.Remove(key);

    public string GetPreference(string key, string defaultValue = "") =>
        _prefs.TryGetValue(key, out var v) ? v : defaultValue;

    public void SetPreference(string key, string value) => _prefs[key] = value;

    public void RemovePreference(string key) => _prefs.Remove(key);

    /// <summary>Directly seeds a secure value (test setup helper).</summary>
    public void SeedSecure(string key, string value) => _secure[key] = value;

    /// <summary>Directly seeds a preference value (test setup helper).</summary>
    public void SeedPreference(string key, string value) => _prefs[key] = value;
}
