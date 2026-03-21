using Omni.Client.Abstractions;

namespace Omni.Client.Services;

/// <summary>
/// MAUI implementation of <see cref="ITokenStorage"/> that delegates to
/// <see cref="SecureStorage"/> (Keychain / Android Keystore) with a
/// <see cref="Preferences"/> fallback for platforms without Keychain entitlements.
/// </summary>
public sealed class MauiTokenStorage : ITokenStorage
{
    public Task<string?> GetSecureAsync(string key) =>
        SecureStorage.Default.GetAsync(key)!;

    public async Task SetSecureAsync(string key, string value)
    {
        try
        {
            await SecureStorage.Default.SetAsync(key, value);
        }
        catch
        {
            // e.g. Mac Catalyst without Keychain entitlements – fall through to Preferences
        }
    }

    public void RemoveSecure(string key) =>
        SecureStorage.Default.Remove(key);

    public string GetPreference(string key, string defaultValue = "") =>
        Preferences.Default.Get(key, defaultValue);

    public void SetPreference(string key, string value) =>
        Preferences.Default.Set(key, value);

    public void RemovePreference(string key) =>
        Preferences.Default.Remove(key);
}
