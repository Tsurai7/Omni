using Microsoft.Maui.Controls;
using System.Threading.Tasks;

namespace Omni.Client.Helpers;

/// <summary>
/// Reusable animation helpers inspired by Apple's fluid motion principles.
/// Use these for consistent motion throughout the app.
/// </summary>
public static class AnimationExtensions
{
    /// <summary>
    /// Fade + slide up entrance. Ideal for cards appearing on page load.
    /// </summary>
    public static async Task FadeSlideInAsync(this VisualElement view, uint delay = 0, uint duration = 320)
    {
        view.Opacity = 0;
        view.TranslationY = 18;
        if (delay > 0)
            await Task.Delay((int)delay);
        var fade = view.FadeToAsync(1, duration, Easing.CubicOut);
        var slide = view.TranslateToAsync(0, 0, duration, Easing.CubicOut);
        await Task.WhenAll(fade, slide);
    }

    /// <summary>
    /// Brief scale bounce — use on buttons and interactive elements to acknowledge a tap.
    /// </summary>
    public static async Task ScalePressAsync(this VisualElement view)
    {
        await view.ScaleToAsync(0.94, 80, Easing.CubicIn);
        await view.ScaleToAsync(1.0, 120, Easing.SpringOut);
    }

    /// <summary>
    /// Single pulse — use to draw attention to a number or metric that just updated.
    /// </summary>
    public static async Task PulseOnceAsync(this VisualElement view)
    {
        await view.ScaleToAsync(1.08, 100, Easing.CubicOut);
        await view.ScaleToAsync(1.0, 200, Easing.SpringOut);
    }

    /// <summary>
    /// Staggered FadeSlideIn for a list of views, each delayed by <paramref name="stagger"/> ms.
    /// </summary>
    public static async Task StaggerFadeSlideInAsync(this IEnumerable<VisualElement> views,
        uint stagger = 60, uint duration = 300)
    {
        uint delay = 0;
        var tasks = new List<Task>();
        foreach (var view in views)
        {
            tasks.Add(view.FadeSlideInAsync(delay, duration));
            delay += stagger;
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Fade out and collapse height — use when dismissing cards or banners.
    /// </summary>
    public static async Task FadeCollapseAsync(this VisualElement view, uint duration = 240)
    {
        await view.FadeToAsync(0, duration, Easing.CubicIn);
        view.IsVisible = false;
    }

    /// <summary>
    /// Shake — use for validation errors on inputs.
    /// </summary>
    public static async Task ShakeAsync(this VisualElement view)
    {
        for (int i = 0; i < 3; i++)
        {
            await view.TranslateToAsync(-8, 0, 60, Easing.CubicOut);
            await view.TranslateToAsync(8, 0, 60, Easing.CubicOut);
        }
        await view.TranslateToAsync(0, 0, 60, Easing.CubicOut);
    }

    /// <summary>
    /// Count-up a label from 0 to <paramref name="target"/> over <paramref name="duration"/> ms.
    /// Gives a satisfying score reveal effect.
    /// </summary>
    public static async Task CountUpAsync(this Label label, int target, string suffix = "",
        uint duration = 800)
    {
        var steps = Math.Min(target, 60);
        var stepDuration = (int)(duration / Math.Max(1, steps));
        for (int i = 0; i <= steps; i++)
        {
            var value = (int)Math.Round(target * (double)i / steps);
            label.Text = $"{value}{suffix}";
            await Task.Delay(stepDuration);
        }
        label.Text = $"{target}{suffix}";
    }
}
