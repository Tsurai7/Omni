using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Omni.Client.Models.Usage;

public sealed class UsageStatsItem : INotifyPropertyChanged
{
    public string AppName { get; set; } = "";
    public string Category { get; set; } = "";
    public long TotalSeconds { get; set; }
    public string FormattedTime => TimeSpan.FromSeconds(TotalSeconds).ToString(@"hh\:mm\:ss");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class UsageDateGroup : ObservableCollection<UsageStatsItem>
{
    public string Date { get; }

    public UsageDateGroup(string date, IEnumerable<UsageStatsItem> items) : base(items)
    {
        Date = date;
    }
}
