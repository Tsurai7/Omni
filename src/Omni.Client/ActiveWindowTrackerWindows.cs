using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Omni.Client.Abstractions;

namespace Omni.Client;

public class ActiveWindowTrackerWindows : IActiveWindowTracker, IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private string _lastAppName = "Unknown";
    private string _lastProcessName = "Unknown";
    private string _lastCategory = "None";
    private readonly Stopwatch _stopwatch = new();
    private readonly Dictionary<string, TimeSpan> _appUsage = new();
    private readonly Dictionary<string, TimeSpan> _categoryUsage = new();
    private readonly object _lock = new();
    private CancellationTokenSource _trackingCts;
    private bool _disposed;

    public void StartTracking()
    {
        if (_trackingCts != null && !_trackingCts.IsCancellationRequested)
            return;

        _trackingCts = new CancellationTokenSource();
        Task.Run(() => TrackActiveWindow(_trackingCts.Token), _trackingCts.Token);
    }

    private async Task TrackActiveWindow(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var (processName, appName) = GetActiveAppInfoSafe();
                var currentCategory = CategoryResolver.ResolveCategory(appName);

                if (appName != _lastAppName || currentCategory != _lastCategory)
                {
                    lock (_lock)
                    {
                        if (_stopwatch.IsRunning)
                        {
                            _stopwatch.Stop();
                            UpdateUsageDictionaries();
                        }

                        _lastAppName = appName;
                        _lastProcessName = processName;
                        _lastCategory = currentCategory;
                        _stopwatch.Restart();
                    }
                }

                await Task.Delay(1500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tracking error: {ex.Message}");
                await Task.Delay(1500, cancellationToken);
            }
        }
    }

    private void UpdateUsageDictionaries()
    {
        if (!string.IsNullOrEmpty(_lastAppName))
        {
            _appUsage[_lastAppName] = _appUsage.TryGetValue(_lastAppName, out var currentTime)
                ? currentTime + _stopwatch.Elapsed
                : _stopwatch.Elapsed;
        }

        if (!string.IsNullOrEmpty(_lastCategory))
        {
            _categoryUsage[_lastCategory] = _categoryUsage.TryGetValue(_lastCategory, out var currentTime)
                ? currentTime + _stopwatch.Elapsed
                : _stopwatch.Elapsed;
        }
    }

    public Dictionary<string, TimeSpan> GetAppUsage()
    {
        lock (_lock)
        {
            return new Dictionary<string, TimeSpan>(_appUsage);
        }
    }

    public Dictionary<string, TimeSpan> GetCategoryUsage()
    {
        lock (_lock)
        {
            return new Dictionary<string, TimeSpan>(_categoryUsage);
        }
    }

    public string GetCurrentCategory()
    {
        lock (_lock)
        {
            return string.IsNullOrEmpty(_lastCategory) || _lastCategory == "None" ? "Other" : _lastCategory;
        }
    }

    public string GetCurrentAppName()
    {
        lock (_lock)
        {
            return string.IsNullOrEmpty(_lastAppName) ? "Unknown" : _lastAppName;
        }
    }

    public string GetProcessNameForApp(string appName)
    {
        lock (_lock)
        {
            return _lastAppName == appName ? _lastProcessName : "Unknown";
        }
    }

    private (string ProcessName, string AppName) GetActiveAppInfoSafe()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return ("Unknown", "Unknown");

            GetWindowThreadProcessId(hwnd, out var processId);
            using var process = Process.GetProcessById((int)processId);
            
            var processName = process.ProcessName.ToLowerInvariant();
            var appName = GetFriendlyAppName(processName);
            
            var instances = Process.GetProcessesByName(process.ProcessName).Length;
            if (instances > 1)
            {
                appName += $" ({instances})";
            }

            return (processName, appName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetActiveAppInfo error: {ex.Message}");
            return ("Unknown", "Unknown");
        }
    }

    private static string GetFriendlyAppName(string processName)
    {
        return processName switch
        {
            "chrome" => "Google Chrome",
            "msedge" => "Microsoft Edge",
            "firefox" => "Firefox",
            "devenv" => "Visual Studio",
            "rider" => "JetBrains Rider",
            "vscode" => "VS Code",
            "winword" => "Microsoft Word",
            "excel" => "Microsoft Excel",
            "powerpnt" => "Microsoft PowerPoint",
            "notepad" => "Notepad",
            "explorer" => "Проводник",
            "steam" => "Steam",
            "discord" => "Discord",
            "telegram" => "Telegram",
            "whatsapp" => "WhatsApp",
            "msmpeng" => "Antimalware Service Executable",
            "dotnet" => ".NET Host",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(processName)
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _trackingCts?.Cancel();
        _trackingCts?.Dispose();
        _stopwatch.Stop();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}