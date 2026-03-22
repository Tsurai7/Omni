using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Models.Productivity;

namespace Omni.Client;

public partial class DigestPage : ContentPage
{
    private readonly IProductivityService _productivityService;
    private string? _digestNotificationId;

    public DigestPage()
    {
        InitializeComponent();
        _productivityService = MauiProgram.AppServices?.GetService<IProductivityService>()
            ?? throw new InvalidOperationException("IProductivityService not registered.");
    }

    public void LoadDigest(NotificationItem digest)
    {
        _digestNotificationId = digest.Id;

        var created = digest.CreatedAt?.ToString("MMM d") ?? "This week";
        DigestDateLabel.Text = $"Week of {created}";

        // Parse action_payload for rich data
        if (digest.ActionPayload.HasValue && digest.ActionPayload.Value.ValueKind == JsonValueKind.Object)
        {
            var payload = digest.ActionPayload.Value;

            if (payload.TryGetProperty("total_focus_hours", out var hoursEl))
            {
                var hours = hoursEl.GetDouble();
                HeadlineValueLabel.Text = hours.ToString("F1") + "h";
            }
            else
            {
                HeadlineValueLabel.Text = "—";
            }

            if (payload.TryGetProperty("focus_change_pct", out var changePctEl))
            {
                var pct = changePctEl.GetDouble();
                ComparisonLabel.Text = pct >= 0
                    ? $"↑ {pct:F0}% vs last week"
                    : $"↓ {Math.Abs(pct):F0}% vs last week";
                ComparisonLabel.TextColor = pct >= 0
                    ? Microsoft.Maui.Graphics.Color.FromArgb("#4ECCA3")
                    : Microsoft.Maui.Graphics.Color.FromArgb("#E07A5F");
                ComparisonLabel.IsVisible = true;
            }

            if (payload.TryGetProperty("avg_daily_score", out var scoreEl))
                AvgScoreLabel.Text = scoreEl.GetInt32().ToString();

            if (payload.TryGetProperty("most_productive_day", out var dayEl))
                BestDayLabel.Text = dayEl.GetString() ?? "—";

            if (payload.TryGetProperty("top_focus_app", out var appEl))
                TopAppLabel.Text = appEl.GetString() ?? "—";

            if (payload.TryGetProperty("streak_days", out var streakEl))
                StreakLabel.Text = streakEl.GetInt32().ToString();

            if (payload.TryGetProperty("insight", out var insightEl))
            {
                var insight = insightEl.GetString();
                if (!string.IsNullOrWhiteSpace(insight))
                {
                    InsightLabel.Text = insight;
                    InsightCard.IsVisible = true;
                }
            }
        }
        else
        {
            // Fallback to title/body text
            HeadlineValueLabel.Text = digest.Title ?? "Weekly Digest";
            if (!string.IsNullOrWhiteSpace(digest.Body))
            {
                InsightLabel.Text = digest.Body;
                InsightCard.IsVisible = true;
            }
        }
    }

    private async void OnDismissClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_digestNotificationId))
        {
            try { await _productivityService.MarkAsReadAsync(_digestNotificationId); }
            catch { /* best effort */ }
        }
        await Shell.Current.GoToAsync("..");
    }
}
