using Omni.Client.Services;

namespace Omni.Client;

/// <summary>
/// Keys and defaults for productivity preferences (stored via Preferences).
/// </summary>
public static class ProductivityPreferences
{
    public const string DailyGoalMinutesKey = "omni_daily_goal_min";
    public const string NotificationIntensityKey = "omni_notification_intensity";
    public const string StreakVisibleKey = "omni_streak_visible";
    public const string FocusHoursStartKey = "omni_focus_hours_start";
    public const string FocusHoursEndKey = "omni_focus_hours_end";
    public const string ThemeKey = "omni_app_theme";

    public const int DefaultDailyGoalMinutes = 60;
    public const string DefaultNotificationIntensity = "Medium";
    public const bool DefaultStreakVisible = true;
    public const string DefaultFocusHoursStart = "09:00";
    public const string DefaultFocusHoursEnd = "17:00";

    public static int GetDailyGoalMinutes() =>
        Preferences.Default.Get(DailyGoalMinutesKey, DefaultDailyGoalMinutes);

    public static void SetDailyGoalMinutes(int value) =>
        Preferences.Default.Set(DailyGoalMinutesKey, value);

    public static string GetNotificationIntensity() =>
        Preferences.Default.Get(NotificationIntensityKey, DefaultNotificationIntensity);

    public static void SetNotificationIntensity(string value) =>
        Preferences.Default.Set(NotificationIntensityKey, value);

    public static bool GetStreakVisible() =>
        Preferences.Default.Get(StreakVisibleKey, DefaultStreakVisible);

    public static void SetStreakVisible(bool value) =>
        Preferences.Default.Set(StreakVisibleKey, value);

    public static string GetFocusHoursStart() =>
        Preferences.Default.Get(FocusHoursStartKey, DefaultFocusHoursStart);

    public static void SetFocusHoursStart(string value) =>
        Preferences.Default.Set(FocusHoursStartKey, value);

    public static string GetFocusHoursEnd() =>
        Preferences.Default.Get(FocusHoursEndKey, DefaultFocusHoursEnd);

    public static void SetFocusHoursEnd(string value) =>
        Preferences.Default.Set(FocusHoursEndKey, value);

    public static AppColorTheme GetTheme() =>
        (AppColorTheme)Preferences.Default.Get(ThemeKey, (int)AppColorTheme.Dark);

    public static void SetTheme(AppColorTheme value) =>
        Preferences.Default.Set(ThemeKey, (int)value);
}
