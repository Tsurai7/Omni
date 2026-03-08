using System.Linq;
using Microsoft.Maui.Graphics;

namespace Omni.Client.Controls;

/// <summary>Draws a horizontal bar chart. Set Segments and MaxValue (or it uses max of values).</summary>
public sealed class UsageBarDrawable : IDrawable
{
    private static readonly Color[] BarColors =
    {
        Color.FromArgb("#4ECCA3"),
        Color.FromArgb("#6C9BC7"),
        Color.FromArgb("#E8A87C"),
        Color.FromArgb("#C38D9E"),
        Color.FromArgb("#85CDCA"),
        Color.FromArgb("#E27D60"),
        Color.FromArgb("#9B59B6"),
        Color.FromArgb("#F4D03F"),
    };

    public IList<ChartSegment> Segments { get; set; } = new List<ChartSegment>();
    public double MaxValue { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Segments.Count == 0)
            return;

        double max = MaxValue > 0 ? MaxValue : Segments.Max(s => s.Value);
        if (max <= 0) return;

        float paddingLeft = 8;
        float paddingRight = 12;
        float labelWidth = 90;
        float barHeight = 20;
        float gap = 10;
        float chartWidth = dirtyRect.Width - labelWidth - paddingLeft - paddingRight;
        float y = 12;

        canvas.FontSize = 11;
        canvas.FontColor = Color.FromArgb("#E0E0E0");

        for (int i = 0; i < Segments.Count; i++)
        {
            var seg = Segments[i];
            var color = BarColors[i % BarColors.Length];
            float barW = (float)(seg.Value / max * chartWidth);
            if (barW < 2 && seg.Value > 0) barW = 2;

            // Label
            canvas.DrawString(TruncateLabel(seg.Label, 16), paddingLeft, y, labelWidth, barHeight, HorizontalAlignment.Left, VerticalAlignment.Center);

            // Bar background
            float barX = labelWidth + paddingLeft;
            canvas.FillColor = Color.FromArgb("#2D2D2D");
            canvas.FillRoundedRectangle(barX, y, chartWidth, barHeight, 4);

            // Bar fill
            canvas.FillColor = color;
            canvas.FillRoundedRectangle(barX, y, barW, barHeight, 4);

            // Value at end of bar
            var timeStr = FormatTime((long)seg.Value);
            canvas.FontColor = Color.FromArgb("#A0A0A0");
            canvas.DrawString(timeStr, barX + barW + 6, y, chartWidth - barW - 6, barHeight, HorizontalAlignment.Left, VerticalAlignment.Center);

            y += barHeight + gap;
        }
    }

    private static string TruncateLabel(string label, int maxLen = 16) =>
        label.Length <= maxLen ? label : label[..(maxLen - 2)] + "..";

    private static string FormatTime(long totalSeconds)
    {
        var t = TimeSpan.FromSeconds(totalSeconds);
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1)
            return $"{(int)t.TotalMinutes}m";
        return $"{t.Seconds}s";
    }
}
