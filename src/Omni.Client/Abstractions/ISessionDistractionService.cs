namespace Omni.Client.Abstractions;

/// <summary>
/// Tracks distraction during an active focus session and computes concentration score on stop.
/// </summary>
public interface ISessionDistractionService
{
    /// <summary>Fired when distraction is detected (distracting category or frequent switching). Debounced.</summary>
    event Action<DistractionEvent>? DistractionDetected;

    /// <summary>Fired when session is stopped with final concentration score and summary.</summary>
    event Action<SessionScoreResult>? SessionEndedWithScore;

    /// <summary>Whether a session is currently being tracked.</summary>
    bool IsRunning { get; }

    /// <summary>Start tracking. Call when user starts a focus session.</summary>
    void Start(DateTime sessionStartTimeUtc, string activityName);

    /// <summary>Stop tracking and compute score. Raises SessionEndedWithScore.</summary>
    void Stop();
}

/// <summary>Reason and context for a distraction event.</summary>
public sealed class DistractionEvent
{
    public string Reason { get; init; } = ""; // "distracting_category" or "frequent_switching"
    public string? ActivityName { get; init; }
    public string? CategoryOrDetail { get; init; }
}

/// <summary>Result when a session ends.</summary>
public sealed class SessionScoreResult
{
    public int Score { get; init; }       // 0-100
    public string Summary { get; init; } = "";
    public long TotalSeconds { get; init; }
    public long DistractingSeconds { get; init; }
    public int DistractionEventCount { get; init; }
}
