using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Omni.Client.Models;

public class AppUsageInfo : INotifyPropertyChanged
{
    private static readonly Dictionary<string, Color> CategoryColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Gaming"] = Color.FromArgb("#FF6B6B"),
        ["Browsing"] = Color.FromArgb("#4ECDC4"),
        ["Coding"] = Color.FromArgb("#45B7D1"),
        ["Messaging"] = Color.FromArgb("#A78BFA"),
        ["Chilling"] = Color.FromArgb("#FF9E7D"),
        ["Productivity"] = Color.FromArgb("#98C379"),
    };

    public string AppName { get; set; }

    private string _category;
    public string Category
    {
        get => _category;
        set
        {
            if (_category == value) return;
            _category = value;
            _categoryColor = value != null && CategoryColors.TryGetValue(value, out var c) ? c : Colors.Gray;
        }
    }
    private TimeSpan _runningTime;
    private Color _categoryColor = Colors.Gray;

    public string FormattedTime => _runningTime.ToString(@"hh\:mm\:ss");

    public Color CategoryColor => _categoryColor;
    
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