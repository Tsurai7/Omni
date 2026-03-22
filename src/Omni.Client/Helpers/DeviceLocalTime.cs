namespace Omni.Client.Helpers;

internal static class DeviceLocalTime
{
    /// <summary>Local offset from UTC in minutes; aligns API date filters with <see cref="DateTime.Today"/>.</summary>
    public static int UtcOffsetMinutes => (int)DateTimeOffset.Now.Offset.TotalMinutes;
}
