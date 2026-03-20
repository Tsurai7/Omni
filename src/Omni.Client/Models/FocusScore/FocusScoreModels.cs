using System.Text.Json.Serialization;

namespace Omni.Client.Models.FocusScore;

public class FocusScoreResponse
{
    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("breakdown")]
    public FocusScoreBreakdown Breakdown { get; set; } = new();

    [JsonPropertyName("trend")]
    public string Trend { get; set; } = "flat";

    [JsonPropertyName("focus_minutes_today")]
    public int FocusMinutesToday { get; set; }

    [JsonPropertyName("sessions_today")]
    public int SessionsToday { get; set; }

    [JsonPropertyName("streak_days")]
    public int StreakDays { get; set; }
}

public class FocusScoreBreakdown
{
    [JsonPropertyName("focus_ratio")]
    public int FocusRatio { get; set; }

    [JsonPropertyName("session_completion")]
    public int SessionCompletion { get; set; }

    [JsonPropertyName("distraction_penalty")]
    public int DistractionPenalty { get; set; }

    [JsonPropertyName("consistency_bonus")]
    public int ConsistencyBonus { get; set; }
}
