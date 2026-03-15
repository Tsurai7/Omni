using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Omni.Client.Models;

public class AppUsage : INotifyPropertyChanged
{
    private TimeSpan _timeSpent;

    public required string Name { get; set; }

    public TimeSpan TimeSpent
    {
        get => _timeSpent;
        set
        {
            if (_timeSpent != value)
            {
                _timeSpent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedTime));
            }
        }
    }

    public required string Category { get; set; }
    public string FormattedTime
    {
        get
        {
            var ts = TimeSpent;
            if (ts.TotalMinutes < 1) return "<1 min";
            if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes} min";
            return $"{(int)ts.TotalHours}h {ts.Minutes}min";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}