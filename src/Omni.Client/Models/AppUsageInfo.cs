using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Omni.Client.Models;

public class AppUsageInfo : INotifyPropertyChanged
{
    public string AppName { get; set; }
    public string Category { get; set; }
    private TimeSpan _runningTime;
    public string FormattedTime => _runningTime.ToString(@"hh\:mm\:ss");
    
    public Color CategoryColor => GetCategoryColor();
    
    private Color GetCategoryColor()
    {
        return Category switch
        {
            "Gaming" => Color.FromArgb("#FF6B6B"),
            "Browsing" => Color.FromArgb("#4ECDC4"),
            "Coding" => Color.FromArgb("#45B7D1"),
            "Messaging" => Color.FromArgb("#A78BFA"),
            "Chilling" => Color.FromArgb("#FF9E7D"),
            "Productivity" => Color.FromArgb("#98C379"),
            _ => Colors.Gray
        };
    }
    
    public TimeSpan RunningTime
    {
        get => _runningTime;
        set
        {
            if (_runningTime != value)
            {
                _runningTime = value;
                OnPropertyChanged();
                // При изменении времени обновляем форматированную строку
                OnPropertyChanged(nameof(FormattedTime));
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}