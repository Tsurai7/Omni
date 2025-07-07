using System.Collections.ObjectModel;

namespace Omni.Client.Models;

public class AppUsageGroup : ObservableCollection<AppUsageInfo>
{
    public string Category { get; }

    public AppUsageGroup(string category, IEnumerable<AppUsageInfo> items) : base(items)
    {
        Category = category;
    }
}