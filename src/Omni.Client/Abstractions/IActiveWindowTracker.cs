namespace Omni.Client.Abstractions;

public interface IActiveWindowTracker
{
    void StartTracking();
    Dictionary<string, TimeSpan> GetAppUsage();
    /// <summary>Current foreground app name, or "Unknown" when not available.</summary>
    string GetCurrentAppName();
    /// <summary>Current app category (e.g. Coding, Gaming), or "Other" when not available.</summary>
    string GetCurrentCategory();
}