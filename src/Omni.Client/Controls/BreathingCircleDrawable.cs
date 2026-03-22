namespace Omni.Client.Controls;

/// <summary>
/// Sinusoidal expanding/contracting circle for the pre-session breathing ritual.
/// Drive by calling <see cref="SetPhase"/> from a DispatcherTimer (50ms interval).
/// Phase 0.0 = fully exhaled (small), 0.5 = fully inhaled (large).
/// </summary>
public class BreathingCircleDrawable : IDrawable
{
    private double _phase;   // 0.0 - 1.0
    private bool _isInhale;

    public bool IsInhale => _isInhale;

    public void SetPhase(double phase)
    {
        _phase = Math.Clamp(phase, 0, 1);
        _isInhale = _phase <= 0.5;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var cx = dirtyRect.Width / 2f;
        var cy = dirtyRect.Height / 2f;
        var maxRadius = Math.Min(cx, cy) - 4f;
        var minRadius = maxRadius * 0.45f;

        // Smooth sinusoidal interpolation
        var t = (float)(0.5 - 0.5 * Math.Cos(_phase * Math.PI * 2));
        var radius = minRadius + t * (maxRadius - minRadius);

        // Outer glow ring
        var glowColor = Color.FromRgba(78, 204, 163, (int)(30 + t * 40));
        canvas.FillColor = glowColor;
        canvas.FillCircle(cx, cy, radius + 10);

        // Main circle
        var alpha = (int)(160 + t * 80);
        canvas.FillColor = Color.FromRgba(78, 204, 163, alpha);
        canvas.FillCircle(cx, cy, radius);

        // Center label
        canvas.FontColor = Color.FromArgb("#0F1210");
        canvas.FontSize = 15;
        var label = _isInhale ? "inhale" : "exhale";
        canvas.DrawString(label, cx - 50, cy - 12, 100, 24,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}
