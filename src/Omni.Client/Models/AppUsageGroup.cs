using System.Collections.ObjectModel;

namespace Omni.Client.Models;

public class AppUsageGroup : ObservableCollection<AppUsageInfo>
{
    private static readonly Dictionary<string, Color> CategoryColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Gaming"]       = Color.FromArgb("#FF6B6B"),
        ["Browsing"]     = Color.FromArgb("#4ECDC4"),
        ["Coding"]       = Color.FromArgb("#45B7D1"),
        ["Messaging"]    = Color.FromArgb("#A78BFA"),
        ["Chilling"]     = Color.FromArgb("#FF9E7D"),
        ["Productivity"] = Color.FromArgb("#98C379"),
    };

    public string Category { get; }
    public Color CategoryColor => CategoryColors.TryGetValue(Category, out var c) ? c : Colors.Gray;

    public AppUsageGroup(string category, IEnumerable<AppUsageInfo> items) : base(items)
    {
        Category = category;
    }
}