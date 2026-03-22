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

            using var path = BuildPieSlicePath(cx, cy, radius, startAngle, sweep);
            canvas.FillColor = color;
            canvas.FillPath(path);

            startAngle += sweep;
        }

        // Donut hole
        canvas.FillColor = Color.FromArgb("#1E1E1E");
        canvas.FillCircle(cx, cy, radius * 0.5f);
        canvas.StrokeColor = Color.FromArgb("#2D2D2D");
        canvas.StrokeSize = 1;
        canvas.DrawCircle(cx, cy, radius * 0.5f);

        // Legend — drawn below the pie in two columns to fit within any width
        float legendRowH = 18f;
        float legendDotR = 4f;
        float colWidth = dirtyRect.Width / 2f;
        float legendStartY = cy + radius + 14f;
        canvas.FontSize = 11;
        colorIndex = 0;
        int itemIndex = 0;
        foreach (var seg in Segments)
        {
            if (seg.Value <= 0) continue;
            var color = SegmentColors[colorIndex % SegmentColors.Length];
            colorIndex++;

            int col = itemIndex % 2;
            int row = itemIndex / 2;
            float lx = col * colWidth + 8f;
            float ly = legendStartY + row * legendRowH;

            canvas.FillColor = color;
            canvas.FillCircle(lx + legendDotR, ly + legendRowH / 2f, legendDotR);

            canvas.FontColor = Color.FromArgb("#E0E0E0");
            var pct = Total > 0 ? (seg.Value / Total * 100) : 0;
            var text = $"{TruncateLabel(seg.Label)} {pct:F0}%";
            canvas.DrawString(text, lx + legendDotR * 2 + 6, ly, colWidth - legendDotR * 2 - 14, legendRowH, HorizontalAlignment.Left, VerticalAlignment.Center);

            itemIndex++;
        }
    }

    /// <summary>
    /// Builds a pie-slice path using cubic Bezier arc approximation.
    /// Avoids MAUI's platform-inconsistent arc APIs entirely.
    /// </summary>
    private static PathF BuildPieSlicePath(float cx, float cy, float radius, float startDeg, float sweepDeg)
    {
        var path = new PathF();

        float startRad = startDeg * MathF.PI / 180f;
        path.MoveTo(cx, cy);
        path.LineTo(cx + radius * MathF.Cos(startRad), cy + radius * MathF.Sin(startRad));

        // Approximate arc in up-to-90° cubic Bezier steps
        float remaining = sweepDeg;
        float angle = startRad;
        while (remaining > 0f)
        {
            float stepDeg = MathF.Min(remaining, 90f);
            float stepRad = stepDeg * MathF.PI / 180f;
            float endAngle = angle + stepRad;

            // k: Bezier control-point scalar for circular arcs
            float k = (4f / 3f) * MathF.Tan(stepRad / 4f) * radius;

            float cos0 = MathF.Cos(angle), sin0 = MathF.Sin(angle);
            float cos1 = MathF.Cos(endAngle), sin1 = MathF.Sin(endAngle);

            // Tangent direction (clockwise in Y-down screen coords): (-sin, cos)
            path.CurveTo(
                cx + radius * cos0 + k * (-sin0), cy + radius * sin0 + k * cos0,  // CP1
                cx + radius * cos1 - k * (-sin1), cy + radius * sin1 - k * cos1,  // CP2
                cx + radius * cos1,               cy + radius * sin1              // end
            );

            angle = endAngle;
            remaining -= stepDeg;
        }

        path.LineTo(cx, cy);
        path.Close();
        return path;
    }

    private static string TruncateLabel(string label, int maxLen = 14) =>
        label.Length <= maxLen ? label : label[..(maxLen - 2)] + "..";
}
