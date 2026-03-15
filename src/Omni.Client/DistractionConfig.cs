namespace Omni.Client;

/// <summary>
/// Configuration for distraction detection during focus sessions.
/// </summary>
public sealed class DistractionConfig
{
    /// <summary>Categories that count as distracting (e.g. Gaming, Chilling, Messaging).</summary>
    public IReadOnlySet<string> DistractingCategories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Gaming",
        "Chilling",
        "Messaging"
    };

    /// <summary>Time window in minutes for counting app switches.</summary>
    public int FrequentSwitchWindowMinutes { get; } = 5;

    /// <summary>Number of app/category switches in the window that triggers "frequent switching".</summary>
    public int FrequentSwitchThreshold { get; } = 3;

    /// <summary>Minimum minutes between distraction notifications to avoid spam.</summary>
    public int NotificationDebounceMinutes { get; } = 2;

    /// <summary>Penalty points subtracted from concentration score per distraction event (e.g. 5).</summary>
    public int ScorePenaltyPerDistractionEvent { get; } = 5;
}
