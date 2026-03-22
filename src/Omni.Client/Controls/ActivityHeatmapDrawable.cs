namespace Omni.Client.Controls;

/// <summary>
/// GitHub contribution graph-style daily activity heatmap.
/// Shows 7 columns (Mon-Sun) × N weeks of focus intensity.
/// Feed data via <see cref="SetData"/> — each entry is (date, focusMinutes).
/// </summary>
public class ActivityHeatmapDrawable : IDrawable
{
    private readonly Dictionary<string, int> _data = new();   // date (yyyy-MM-dd) → focus minutes
    private static readonly Color[] HeatColors =
    {
        Color.FromArgb("#1A1A1F"),   // 0 = no activity
        Color.FromArgb("#1A4A38"),   // 1-29 min
        Color.FromArgb("#2A7A61"),   // 30-59 min
        Color.FromArgb("#3AAA87"),   // 60-89 min
        Color.FromArgb("#4ECCA3"),   // 90+ min
    };

    public void SetData(IEnumerable<(string Date, int FocusMinutes)> data)
    {
        _data.Clear();
        foreach (var (d, m) in data)
            _data[d] = m;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        const int cols = 13; // weeks back
        const float cellSize = 14f;
        const float gap = 3f;
        const float step = cellSize + gap;
        const float padL = 20f, padT = 20f;

        var today = DateTime.Today;

        // Day-of-week labels
        var dayLabels = new[] { "M", "T", "W", "T", "F", "S", "S" };
        canvas.FontColor = Color.FromArgb("#66667A");
        canvas.FontSize = 9;
        for (int row = 0; row < 7; row++)
            canvas.DrawString(dayLabels[row], 0, padT + row * step, padL - 2, cellSize,
                HorizontalAlignment.Right, VerticalAlignment.Center);

        // Draw cells
        for (int col = 0; col < cols; col++)
        {
            for (int row = 0; row < 7; row++)
            {
                // Calculate date for this cell (col 0 = oldest week, col cols-1 = this week)
                var daysBack = (cols - 1 - col) * 7 + (6 - row);
                var date = today.AddDays(-daysBack);
                if (date > today) continue;

                var dateKey = date.ToString("yyyy-MM-dd");
                var minutes = _data.TryGetValue(dateKey, out var m) ? m : 0;
                var colorIndex = minutes switch
                {
                    0         => 0,
                    < 30      => 1,
                    < 60      => 2,
                    < 90      => 3,
                    _         => 4,
                };

                var x = padL + col * step;
                var y = padT + row * step;

                canvas.FillColor = HeatColors[colorIndex];
                canvas.FillRoundedRectangle(x, y, cellSize, cellSize, 3);
            }
        }

        // Legend
        var legendX = padL;
        var legendY = padT + 7 * step + 8f;
        canvas.FontColor = Color.FromArgb("#66667A");
        canvas.FontSize = 9;
        canvas.DrawString("Less", legendX, legendY, 30, 12,
            HorizontalAlignment.Left, VerticalAlignment.Center);
        for (int i = 0; i < HeatColors.Length; i++)
        {
            canvas.FillColor = HeatColors[i];
            canvas.FillRoundedRectangle(legendX + 36 + i * (cellSize + 2), legendY, cellSize, cellSize, 3);
        }
        canvas.DrawString("More", legendX + 36 + HeatColors.Length * (cellSize + 2) + 4, legendY, 30, 12,
            HorizontalAlignment.Left, VerticalAlignment.Center);
    }
}
