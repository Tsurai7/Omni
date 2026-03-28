namespace Omni.Client.Controls;

/// <summary>
/// Vertical bar chart showing total focus minutes for each weekday (Mon–Sun).
/// Feed data via <see cref="SetData"/> before assigning to a GraphicsView.
/// </summary>
public class WeeklyFocusBarDrawable : IDrawable
{
    private static readonly string[] DayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private readonly Color _barColor     = Color.FromArgb("#4ECCA3");
    private readonly Color _barDimColor  = Color.FromArgb("#1E3D34");
    private readonly Color _gridColor    = Color.FromArgb("#2A2A32");
    private readonly Color _labelColor   = Color.FromArgb("#66667A");
    private readonly Color _valueDim     = Color.FromArgb("#4A4A58");

    // focusSecondsByDay[0]=Mon … [6]=Sun
    private long[] _data = new long[7];
    private int _todayIndex = -1;

    /// <param name="focusSecondsByDay">Array of 7 longs: index 0 = Monday … 6 = Sunday.</param>
    /// <param name="todayIndex">Index (0-based Mon) of today, or -1 if not applicable.</param>
    public void SetData(long[] focusSecondsByDay, int todayIndex = -1)
    {
        _data = focusSecondsByDay;
        _todayIndex = todayIndex;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        const float padL = 8f, padR = 8f, padT = 20f, padB = 26f;
        var w = dirtyRect.Width - padL - padR;
        var h = dirtyRect.Height - padT - padB;

        var maxVal = _data.Length > 0 ? _data.Max() : 0L;
        if (maxVal <= 0) maxVal = 1;

        // Subtle horizontal grid lines (3 levels)
        canvas.StrokeColor = _gridColor;
        canvas.StrokeSize = 1;
        for (int g = 1; g <= 3; g++)
        {
            var gy = padT + h - g / 3f * h;
            canvas.DrawLine(padL, gy, padL + w, gy);
        }

        var slotW = w / 7f;
        var barW  = slotW * 0.52f;

        for (int i = 0; i < 7; i++)
        {
            var val  = _data[i];
            var barH = (float)(val / (double)maxVal * h);
            if (barH < 2 && val > 0) barH = 2;

            var cx   = padL + i * slotW + slotW / 2f;
            var barX = cx - barW / 2f;
            var barY = padT + h - barH;

            bool isToday = i == _todayIndex;

            // Bar fill
            canvas.FillColor = isToday ? _barColor : _barDimColor;
            if (barH > 0)
                canvas.FillRoundedRectangle(barX, barY, barW, barH, 3);

            // Value label above bar (skip if 0)
            if (val > 0)
            {
                canvas.FontColor = isToday ? _barColor : _valueDim;
                canvas.FontSize  = 8;
                canvas.DrawString(
                    FormatTime(val),
                    cx - slotW / 2f, barY - 16f, slotW, 16f,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }

            // Day label below bar
            canvas.FontColor = isToday ? _barColor : _labelColor;
            canvas.FontSize  = 9;
            canvas.DrawString(
                DayLabels[i],
                cx - slotW / 2f, padT + h + 4f, slotW, 16f,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    private static string FormatTime(long totalSeconds)
    {
        if (totalSeconds <= 0) return "";
        var t = TimeSpan.FromSeconds(totalSeconds);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m";
        return $"{t.Seconds}s";
    }
}
