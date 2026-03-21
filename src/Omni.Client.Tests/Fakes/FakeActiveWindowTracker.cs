using Omni.Client.Abstractions;

namespace Omni.Client.Tests.Fakes;

/// <summary>Controllable <see cref="IActiveWindowTracker"/> for unit tests.</summary>
public sealed class FakeActiveWindowTracker : IActiveWindowTracker
{
    public string CurrentAppName { get; set; } = "TestApp";
    public string CurrentCategory { get; set; } = "Coding";
    public bool TrackingStarted { get; private set; }

    public string GetCurrentAppName() => CurrentAppName;
    public string GetCurrentCategory() => CurrentCategory;
    public void StartTracking() => TrackingStarted = true;
    public Dictionary<string, TimeSpan> GetAppUsage() => new();
}
