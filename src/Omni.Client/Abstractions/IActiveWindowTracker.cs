namespace Omni.Client.Abstractions;

public interface IActiveWindowTracker
{
    public void StartTracking();
    public Dictionary<string, TimeSpan> GetAppUsage();
}