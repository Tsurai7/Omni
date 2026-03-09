using Omni.Client.Abstractions;

namespace Omni.Client;

/// <summary>
/// No-op tracker for platforms without active-window APIs (e.g. Android, iOS).
/// Returns "Unknown" / "Other" so distraction logic can run without crashing.
/// </summary>
public class ActiveWindowTrackerStub : IActiveWindowTracker
{
    public void StartTracking() { }

    public Dictionary<string, TimeSpan> GetAppUsage() => new Dictionary<string, TimeSpan>();

    public string GetCurrentAppName() => "Unknown";

    public string GetCurrentCategory() => "Other";
}
