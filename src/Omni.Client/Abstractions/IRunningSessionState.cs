namespace Omni.Client.Abstractions;

/// <summary>
/// Holds the current running focus session state so it survives navigation (e.g. switching to another tab).
/// </summary>
public interface IRunningSessionState
{
    bool IsRunning { get; }
    DateTime StartTimeUtc { get; }
    DateTime? EndTimeUtc { get; }
    string ActivityName { get; }
    string ActivityType { get; }

    /// <summary>Fired every second while running; pass remaining seconds (or null if no end time = elapsed).</summary>
    event Action<int?>? Tick;

    /// <summary>Fired when countdown reaches zero (only for timed sessions). Caller should save session and call Stop().</summary>
    event Action? CountdownReachedZero;

    void Start(string activityName, string activityType, int? durationSeconds);
    void Stop();

    /// <summary>Remaining seconds (countdown), or null if no end time (elapsed mode).</summary>
    int? GetRemainingSeconds();
    /// <summary>Elapsed seconds since start.</summary>
    int GetElapsedSeconds();
}
