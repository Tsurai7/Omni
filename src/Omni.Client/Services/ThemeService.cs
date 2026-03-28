namespace Omni.Client.Services;

public enum AppColorTheme { Dark, Light, System }

public interface IThemeService
{
    AppColorTheme Current { get; }
    void Apply(AppColorTheme theme);
}

public class ThemeService : IThemeService
{
    public AppColorTheme Current { get; private set; } = AppColorTheme.Dark;

    public void Apply(AppColorTheme theme)
    {
        Current = theme;
        ProductivityPreferences.SetTheme(theme);

        Application.Current!.UserAppTheme = theme switch
        {
            AppColorTheme.Light  => AppTheme.Light,
            AppColorTheme.Dark   => AppTheme.Dark,
            _                    => AppTheme.Unspecified
        };

        var palette = theme == AppColorTheme.Light ? LightPalette() : DarkPalette();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var res = Application.Current!.Resources;
            foreach (var (key, value) in palette)
                res[key] = value;
        });
    }

    // ── Dark palette (mirrors ProductivityTokens.xaml) ───────────────────────

    private static Dictionary<string, object> DarkPalette() => new()
    {
        ["ProductivityPageBackground"]        = Color.FromArgb("#07070A"),
        ["ProductivityCardBackground"]         = Color.FromArgb("#0F0F12"),
        ["ProductivityCardBackgroundElevated"] = Color.FromArgb("#16161B"),
        ["ProductivityCardStroke"]             = Color.FromArgb("#12FFFFFF"),
        ["ProductivityCardStrokeSubtle"]       = Color.FromArgb("#0AFFFFFF"),
        ["ProductivityTextPrimary"]            = Color.FromArgb("#F0F0F4"),
        ["ProductivityTextSecondary"]          = Color.FromArgb("#8E8EA0"),
        ["ProductivityTextTertiary"]           = Color.FromArgb("#4A4A5A"),
        ["ProductivityTextMuted"]              = Color.FromArgb("#333340"),
        ["ProductivityUIAccent"]               = Color.FromArgb("#8B5CF6"),
        ["ProductivityUIAccentSoft"]           = Color.FromArgb("#2E8B5CF6"),
        ["ProductivityUIAccentGlow"]           = Color.FromArgb("#598B5CF6"),
        ["ProductivityFocus"]                  = Color.FromArgb("#4ECCA3"),
        ["ProductivityFocusDim"]               = Color.FromArgb("#2A7A61"),
        ["ProductivityNeutral"]                = Color.FromArgb("#6B9AC4"),
        ["ProductivityDistraction"]            = Color.FromArgb("#E07A5F"),
        ["ProductivityAmber"]                  = Color.FromArgb("#F5A623"),
        ["ProductivitySuccess"]                = Color.FromArgb("#4ECCA3"),
        ["ProductivityCtaBackground"]          = Color.FromArgb("#4ECCA3"),
        ["ProductivityCtaText"]                = Color.FromArgb("#0F1210"),
        ["ProductivityDanger"]                 = Color.FromArgb("#E05C5C"),
        ["SidebarBackground"]                  = Color.FromArgb("#0F0F12"),
        ["SidebarItemActiveBg"]                = Color.FromArgb("#1A8B5CF6"),
        ["SidebarItemPressedBg"]               = Color.FromArgb("#0D8B5CF6"),
        ["ProductivityPageGradient"]           = BuildDarkGradient(),
    };

    // ── Light palette ─────────────────────────────────────────────────────────

    private static Dictionary<string, object> LightPalette() => new()
    {
        ["ProductivityPageBackground"]        = Color.FromArgb("#F5F5FA"),
        ["ProductivityCardBackground"]         = Color.FromArgb("#FFFFFF"),
        ["ProductivityCardBackgroundElevated"] = Color.FromArgb("#EDEDF5"),
        ["ProductivityCardStroke"]             = Color.FromArgb("#14000000"),
        ["ProductivityCardStrokeSubtle"]       = Color.FromArgb("#0A000000"),
        ["ProductivityTextPrimary"]            = Color.FromArgb("#1C1C1E"),
        ["ProductivityTextSecondary"]          = Color.FromArgb("#6C6C80"),
        ["ProductivityTextTertiary"]           = Color.FromArgb("#AEAEC0"),
        ["ProductivityTextMuted"]              = Color.FromArgb("#D1D1E0"),
        ["ProductivityUIAccent"]               = Color.FromArgb("#7C3AED"),
        ["ProductivityUIAccentSoft"]           = Color.FromArgb("#157C3AED"),
        ["ProductivityUIAccentGlow"]           = Color.FromArgb("#257C3AED"),
        ["ProductivityFocus"]                  = Color.FromArgb("#00A388"),
        ["ProductivityFocusDim"]               = Color.FromArgb("#DFF7F4"),
        ["ProductivityNeutral"]                = Color.FromArgb("#5080A4"),
        ["ProductivityDistraction"]            = Color.FromArgb("#C05840"),
        ["ProductivityAmber"]                  = Color.FromArgb("#C07810"),
        ["ProductivitySuccess"]                = Color.FromArgb("#00A388"),
        ["ProductivityCtaBackground"]          = Color.FromArgb("#00A388"),
        ["ProductivityCtaText"]                = Color.FromArgb("#FFFFFF"),
        ["ProductivityDanger"]                 = Color.FromArgb("#DC2626"),
        ["SidebarBackground"]                  = Color.FromArgb("#EBEBF2"),
        ["SidebarItemActiveBg"]                = Color.FromArgb("#147C3AED"),
        ["SidebarItemPressedBg"]               = Color.FromArgb("#0A7C3AED"),
        ["ProductivityPageGradient"]           = BuildLightGradient(),
    };

    // ── Gradient builders ─────────────────────────────────────────────────────

    private static LinearGradientBrush BuildDarkGradient() => new()
    {
        StartPoint = new Point(0.5, 0),
        EndPoint   = new Point(0.5, 1),
        GradientStops =
        [
            new GradientStop { Color = Color.FromArgb("#1A8B5CF6"), Offset = 0.00f },
            new GradientStop { Color = Color.FromArgb("#FF07070A"), Offset = 0.38f },
        ]
    };

    private static LinearGradientBrush BuildLightGradient() => new()
    {
        StartPoint = new Point(0.5, 0),
        EndPoint   = new Point(0.5, 1),
        GradientStops =
        [
            new GradientStop { Color = Color.FromArgb("#0D7C3AED"), Offset = 0.00f },
            new GradientStop { Color = Color.FromArgb("#FFF5F5FA"), Offset = 0.28f },
        ]
    };
}
