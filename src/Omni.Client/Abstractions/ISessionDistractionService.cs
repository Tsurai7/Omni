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
public record DistractionEvent(string Reason = "", string? ActivityName = null, string? CategoryOrDetail = null);

/// <summary>Result when a session ends.</summary>
public record SessionScoreResult(
    int Score,
    string Summary = "",
    long TotalSeconds = 0,
    long DistractingSeconds = 0,
    int DistractionEventCount = 0);
