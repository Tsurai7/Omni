namespace Omni.Client.Abstractions;

/// <summary>
/// Abstracts platform-specific secure and preference storage so that AuthService
/// can be tested without the MAUI runtime (SecureStorage / Preferences).
/// </summary>
public interface ITokenStorage
{
    Task<string?> GetSecureAsync(string key);
    Task SetSecureAsync(string key, string value);
    void RemoveSecure(string key);

    string GetPreference(string key, string defaultValue = "");
    void SetPreference(string key, string value);
    void RemovePreference(string key);
}
