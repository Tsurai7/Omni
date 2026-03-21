namespace Omni.Client.Services;

/// <summary>
/// Pure, MAUI-free helper that computes concentration score from session metrics.
/// Extracted from <see cref="SessionDistractionService"/> so the formula can be
/// unit-tested without the MAUI runtime.
/// </summary>
public static class SessionScoreCalculator
{
    /// <summary>
    /// Calculates a concentration score 0–100.
    /// </summary>
    /// <param name="totalSeconds">Total session duration in seconds.</param>
    /// <param name="distractingSeconds">Seconds spent in distracting apps.</param>
    /// <param name="distractionEventCount">Number of distraction notification events fired.</param>
    /// <param name="penaltyPerEvent">Score penalty subtracted per event (from <see cref="DistractionConfig.ScorePenaltyPerDistractionEvent"/>).</param>
    public static int Calculate(
        double totalSeconds,
        double distractingSeconds,
        int distractionEventCount,
        int penaltyPerEvent)
    {
        if (totalSeconds <= 0) return 100;
        var focusedSeconds = totalSeconds - distractingSeconds;
        var baseScore = 100.0 * focusedSeconds / totalSeconds;
        var penalty = distractionEventCount * penaltyPerEvent;
        return (int)Math.Round(Math.Max(0, Math.Min(100, baseScore - penalty)));
    }
}
