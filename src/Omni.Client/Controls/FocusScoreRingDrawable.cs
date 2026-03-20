using Microsoft.Maui.Graphics;

namespace Omni.Client.Controls;

/// <summary>
/// Circular progress ring that renders a 0-100 focus score.
/// Arc spans 270° (from 135° to 405°), with rounded caps and color that shifts
/// red → amber → mint green based on score value.
/// Place in a GraphicsView with equal width/height (e.g. 160×160).
/// </summary>
public class FocusScoreRingDrawable : IDrawable
{
    private int _score;
    private string _trend = "flat";

    public int Score
    {
        get => _score;
        set => _score = Math.Clamp(value, 0, 100);
    }

    public string Trend
    {
        get => _trend;
        set => _trend = value ?? "flat";
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var cx = dirtyRect.Width / 2f;
        var cy = dirtyRect.Height / 2f;
        var radius = Math.Min(cx, cy) - 14f;
        var strokeWidth = 10f;

        // ── Track (background arc) ───────────────────────────────────────────
        canvas.StrokeColor = Color.FromArgb("#2A2A32");
        canvas.StrokeSize = strokeWidth;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawArc(cx - radius, cy - radius, radius * 2, radius * 2,
            startAngle: -225f, endAngle: 45f, clockwise: false, closed: false);

        // ── Score arc ────────────────────────────────────────────────────────
        if (_score > 0)
        {
            var arcColor = GetScoreColor(_score);
            canvas.StrokeColor = arcColor;
            canvas.StrokeSize = strokeWidth;
            canvas.StrokeLineCap = LineCap.Round;

            // 270° total arc; map 0-100 to 0-270°
            var sweepDegrees = (_score / 100f) * 270f;
            canvas.DrawArc(cx - radius, cy - radius, radius * 2, radius * 2,
                startAngle: -225f, endAngle: -225f + sweepDegrees, clockwise: false, closed: false);
        }

        // ── Score number ─────────────────────────────────────────────────────
        var scoreText = _score.ToString();
        canvas.FontColor = Color.FromArgb("#F0F0F2");
        canvas.FontSize = radius * 0.58f;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.DrawString(scoreText, cx - radius, cy - radius * 0.45f, radius * 2, radius,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        // ── Trend indicator ──────────────────────────────────────────────────
        var trendSymbol = _trend switch
        {
            "up"   => "↑",
            "down" => "↓",
            _      => "→"
        };
        var trendColor = _trend switch
        {
            "up"   => Color.FromArgb("#4ECCA3"),
            "down" => Color.FromArgb("#E07A5F"),
            _      => Color.FromArgb("#66667A")
        };
        canvas.FontColor = trendColor;
        canvas.FontSize = radius * 0.22f;
        canvas.DrawString(trendSymbol, cx - radius, cy + radius * 0.22f, radius * 2, radius * 0.4f,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private static Color GetScoreColor(int score)
    {
        if (score < 30)
            return Color.FromArgb("#E07A5F"); // red
        if (score < 60)
            return Color.FromArgb("#F5A623"); // amber
        return Color.FromArgb("#4ECCA3");     // mint
    }
}
