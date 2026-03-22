namespace Omni.Client.Controls;

/// <summary>
/// Line chart rendering daily focus scores over a time period.
/// Feed data via <see cref="SetData"/> before assigning to a GraphicsView.
/// </summary>
public class FocusScoreTrendDrawable : IDrawable
{
    private readonly List<(string Date, int Score)> _points = new();
    private readonly Color _lineColor = Color.FromArgb("#4ECCA3");
    private readonly Color _fillColor = Color.FromRgba(78, 204, 163, 28);
    private readonly Color _gridColor = Color.FromArgb("#2A2A32");
    private readonly Color _labelColor = Color.FromArgb("#66667A");

    public void SetData(IEnumerable<(string Date, int Score)> data)
    {
        _points.Clear();
        _points.AddRange(data);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (_points.Count < 2) return;

        const float padL = 32f, padR = 16f, padT = 12f, padB = 28f;
        var w = dirtyRect.Width - padL - padR;
        var h = dirtyRect.Height - padT - padB;

        // Grid lines at 0, 25, 50, 75, 100
        canvas.StrokeColor = _gridColor;
        canvas.StrokeSize = 1;
        foreach (var grid in new[] { 0, 25, 50, 75, 100 })
        {
            var y = padT + h - (grid / 100f) * h;
            canvas.DrawLine(padL, y, padL + w, y);
            canvas.FontColor = _labelColor;
            canvas.FontSize = 9;
            canvas.DrawString(grid.ToString(), 0, y - 6, padL - 2, 12,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        // Compute point positions
        var pts = new List<PointF>();
        for (int i = 0; i < _points.Count; i++)
        {
            var x = padL + (float)i / (_points.Count - 1) * w;
            var y = padT + h - (_points[i].Score / 100f) * h;
            pts.Add(new PointF(x, y));
        }

        // Fill area under line
        var path = new PathF();
        path.MoveTo(pts[0].X, padT + h);
        path.LineTo(pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Count; i++)
            path.CurveTo(
                (pts[i - 1].X + pts[i].X) / 2, pts[i - 1].Y,
                (pts[i - 1].X + pts[i].X) / 2, pts[i].Y,
                pts[i].X, pts[i].Y);
        path.LineTo(pts[^1].X, padT + h);
        path.Close();
        canvas.FillColor = _fillColor;
        canvas.FillPath(path);

        // Line
        canvas.StrokeColor = _lineColor;
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        for (int i = 0; i < pts.Count - 1; i++)
            canvas.DrawLine(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);

        // Dots
        canvas.FillColor = _lineColor;
        foreach (var pt in pts)
            canvas.FillCircle(pt.X, pt.Y, 3.5f);

        // X axis date labels (show at most 5)
        var step = Math.Max(1, _points.Count / 5);
        canvas.FontColor = _labelColor;
        canvas.FontSize = 9;
        for (int i = 0; i < _points.Count; i += step)
        {
            var x = padL + (float)i / (_points.Count - 1) * w;
            var label = _points[i].Date.Length >= 5 ? _points[i].Date[5..] : _points[i].Date;
            canvas.DrawString(label, x - 14, padT + h + 4, 28, 16,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
