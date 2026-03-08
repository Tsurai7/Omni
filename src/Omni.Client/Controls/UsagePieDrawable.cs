using Microsoft.Maui.Graphics;

namespace Omni.Client.Controls;

/// <summary>Draws a pie chart. Set Segments and total, then assign to GraphicsView.Drawable and call Invalidate().</summary>
public sealed class UsagePieDrawable : IDrawable
{
    private static readonly Color[] SegmentColors =
    {
        Color.FromArgb("#4ECCA3"), // teal
        Color.FromArgb("#6C9BC7"), // blue
        Color.FromArgb("#E8A87C"), // orange
        Color.FromArgb("#C38D9E"), // mauve
        Color.FromArgb("#85CDCA"), // mint
        Color.FromArgb("#E27D60"), // coral
        Color.FromArgb("#9B59B6"), // purple
        Color.FromArgb("#F4D03F"), // yellow
    };

    public IList<ChartSegment> Segments { get; set; } = new List<ChartSegment>();
    public double Total { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Segments.Count == 0 || Total <= 0)
            return;

        float cx = dirtyRect.Width / 2f;
        float cy = dirtyRect.Height / 2f;
        float radius = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f - 28f;
        if (radius < 10) radius = 10;

        float startAngle = -90f; // 12 o'clock
        int colorIndex = 0;

        foreach (var seg in Segments)
        {
            if (seg.Value <= 0) continue;
            float sweep = (float)(360.0 * seg.Value / Total);
            var color = SegmentColors[colorIndex % SegmentColors.Length];
            colorIndex++;

            float startRad = startAngle * (float)Math.PI / 180f;
            float x1 = cx + radius * (float)Math.Cos(startRad);
            float y1 = cy + radius * (float)Math.Sin(startRad);

            using var path = new PathF();
            path.MoveTo(cx, cy);
            path.LineTo(x1, y1);
            path.AddArc(cx - radius, cy - radius, cx + radius, cy + radius, startAngle, startAngle + sweep, clockwise: true);
            path.LineTo(cx, cy);
            path.Close();
            canvas.FillColor = color;
            canvas.FillPath(path);
            canvas.StrokeColor = Color.FromArgb("#2D2D2D");
            canvas.StrokeSize = 1;
            canvas.DrawPath(path);

            startAngle += sweep;
        }

        // Donut hole
        canvas.FillColor = Color.FromArgb("#1E1E1E");
        canvas.FillCircle(cx, cy, radius * 0.5f);
        canvas.StrokeColor = Color.FromArgb("#2D2D2D");
        canvas.StrokeSize = 1;
        canvas.DrawCircle(cx, cy, radius * 0.5f);

        // Legend
        float legendX = cx + radius + 16;
        float legendY = cy - (Segments.Count * 18f) / 2f;
        canvas.FontSize = 11;
        colorIndex = 0;
        foreach (var seg in Segments)
        {
            if (seg.Value <= 0) continue;
            var color = SegmentColors[colorIndex % SegmentColors.Length];
            colorIndex++;
            canvas.FillColor = color;
            canvas.FillCircle(legendX, legendY + 9, 5);
            canvas.FontColor = Color.FromArgb("#E0E0E0");
            var pct = Total > 0 ? (seg.Value / Total * 100) : 0;
            var text = $"{TruncateLabel(seg.Label)} {pct:F0}%";
            canvas.DrawString(text, legendX + 14, legendY, dirtyRect.Width - legendX - 10, 18, HorizontalAlignment.Left, VerticalAlignment.Center);
            legendY += 18;
        }
    }

    private static string TruncateLabel(string label, int maxLen = 14) =>
        label.Length <= maxLen ? label : label[..(maxLen - 2)] + "..";
}
