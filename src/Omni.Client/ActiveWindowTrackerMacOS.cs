using System.Diagnostics;
using Omni.Client.Abstractions;

namespace Omni.Client;

public class ActiveWindowTrackerMacOs : IActiveWindowTracker
{
    private readonly Dictionary<string, DateTime> _startTimes = new();
    private readonly Dictionary<string, TimeSpan> _usage = new();
    private readonly object _lock = new();
    private string _currentApp = "Unknown";
    private string _currentTab = string.Empty;
    private System.Timers.Timer? _timer;
    private bool? _hasAutomationPermission;

    private string _cachedTab = string.Empty;
    private string _cachedTabForApp = string.Empty;
    private DateTime _tabCacheTime = DateTime.MinValue;
    private static readonly TimeSpan TabCacheTtl = TimeSpan.FromSeconds(3);

    /// <summary>Single-line script so <c>osascript -e</c> argv handling is reliable across macOS versions.</summary>
    private const string GetActiveAppScript =
        """tell application "System Events" to get name of first application process whose frontmost is true""";

    private const string GetChromeTabScript =
        """
        tell application "Google Chrome"
                    if it is running then
                        get title of active tab of first window
                    else
                        return ""
                    end if
                end tell
        """;

    private const string GetSafariTabScript =
        """
        tell application "Safari"
                    if it is running then
                        get name of current tab of first window
                    else
                        return ""
                    end if
                end tell
        """;

    private const string GetFirefoxTabScript =
        """
        tell application "Firefox"
                    if it is running then
                        tell first window
                            return name of active tab
                        end tell
                    else
                        return ""
                    end if
                end tell
        """;

    private const string CheckPermissionScript = 
        """tell application "System Events" to get name of processes""";

    public event Action? PermissionDenied;

    public void StartTracking()
    {
        if (_timer != null && _timer.Enabled)
            return;
        VerifyAutomationPermissions();

        // Prime current app immediately so first UI update shows active app (otherwise ~2s delay)
        CheckActiveApp();

        // 2s interval reduces CPU and process spawns (was 1s)
        _timer = new System.Timers.Timer(2000);
        _timer.Elapsed += (s, e) => CheckActiveApp();
        _timer.Start();
    }

    private async void CheckActiveApp()
    {
        try
        {
            var activeApp = GetActiveAppName();
            
            if (activeApp == "PrivilegeError")
            {
                _timer?.Stop();
                PermissionDenied?.Invoke();
                return;
            }

            var activeTab = string.Empty;
            var tabScript = activeApp switch
            {
                "Google Chrome" => GetChromeTabScript,
                "Safari"        => GetSafariTabScript,
                "Firefox"       => GetFirefoxTabScript,
                _               => null
            };
            if (tabScript != null)
            {
                if (activeApp == _cachedTabForApp && DateTime.Now - _tabCacheTime < TabCacheTtl)
                    activeTab = _cachedTab;
                else
                {
                    activeTab = GetActiveTabName(tabScript);
                    _cachedTab = activeTab;
                    _cachedTabForApp = activeApp;
                    _tabCacheTime = DateTime.Now;
                }
            }

            var appIdentifier = string.IsNullOrEmpty(activeTab) ? activeApp : $"{activeApp} - {activeTab}";

            lock (_lock)
            {
                if (appIdentifier != _currentApp)
                {
                    if (!string.IsNullOrEmpty(_currentApp) 
                        && _startTimes.TryGetValue(_currentApp, out var startTime))
                    {
                        UpdateAppUsage(_currentApp, startTime);
                    }

                    _currentApp = appIdentifier;
                    _currentTab = activeTab;
                    _startTimes[_currentApp] = DateTime.Now;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.Print($"Error in CheckActiveApp: {ex.Message}");
        }
    }

    private static string GetActiveTabName(string script)
    {
        try
        {
            var result = ExecuteAppleScript(script);
            return string.IsNullOrWhiteSpace(result) ? string.Empty : result;
        }
        catch
        {
            return string.Empty;
        }
    }

    public Dictionary<string, TimeSpan> GetAppUsage()
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_currentApp) 
                && _startTimes.TryGetValue(_currentApp, out var startTime))
            {
                UpdateAppUsage(_currentApp, startTime);
                _startTimes[_currentApp] = DateTime.Now;
            }
            
            return new Dictionary<string, TimeSpan>(_usage);
        }
    }

    public string GetCurrentAppName()
    {
        lock (_lock)
        {
            return string.IsNullOrEmpty(_currentApp) ? "Unknown" : _currentApp;
        }
    }

    public string GetCurrentCategory()
    {
        lock (_lock)
        {
            var appName = string.IsNullOrEmpty(_currentApp) ? "" : _currentApp;
            var nameForCategory = appName.Contains(" - ") ? appName.Split(" - ")[0] : appName;
            return CategoryResolver.ResolveCategory(nameForCategory);
        }
    }

    private void UpdateAppUsage(string appName, DateTime startTime)
    {
        var elapsed = DateTime.Now - startTime;
        _usage[appName] = _usage.GetValueOrDefault(appName) + elapsed;
    }

    private void VerifyAutomationPermissions()
    {
        if (!CheckAutomationPermission())
        {
            ShowPermissionInstructions();
        }
    }

    private bool CheckAutomationPermission()
    {
        if (_hasAutomationPermission.HasValue)
            return _hasAutomationPermission.Value;

        try
        {
            var result = ExecuteAppleScript(CheckPermissionScript);
            _hasAutomationPermission = !string.IsNullOrEmpty(result) && !result.Contains("error") && !result.Contains("denied");
            return _hasAutomationPermission.Value;
        }
        catch
        {
            _hasAutomationPermission = false;
            return false;
        }
    }

    private void ShowPermissionInstructions()
    {
        const string script = @"
            tell application ""System Events""
                activate
                display dialog ""Для работы приложения необходимо предоставить доступ к System Events и браузерам:

            1. Откройте Системные настройки
            2. Перейдите в Конфиденциальность → Automation
            3. Найдите это приложение
            4. Включите доступ для System Events и используемых браузеров"" buttons {""Открыть настройки"", ""Позже""} default button 1 with icon caution
                if button returned of result is ""Открыть настройки"" then
                    tell application ""System Preferences""
                        activate
                        reveal anchor ""Privacy_Automation"" of pane id ""com.apple.preference.security""
                    end tell
                end if
            end tell";

        ExecuteAppleScript(script);
    }

    private string GetActiveAppName()
    {
        try
        {
            var result = ExecuteAppleScript(GetActiveAppScript);
            
            if (result.Contains("privilege violation") || 
                result.Contains("-10004") || 
                result.Contains("Authorization is denied"))
            {
                return "PrivilegeError";
            }
            
            return string.IsNullOrEmpty(result) ? "Unknown" : result;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static bool _loggedAppleScriptNotRunning;

    private static string ExecuteAppleScript(string script)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    Arguments = $"-e \"{script.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit();

            if (string.IsNullOrEmpty(error))
                return output;

            // -600 = "Application isn't running" (e.g. System Events or a browser not visible to this process, or app not running). Treat as empty, don't spam logs.
            if (error.Contains("-600") || error.Contains("isn't running", StringComparison.OrdinalIgnoreCase))
            {
                if (!_loggedAppleScriptNotRunning)
                {
                    _loggedAppleScriptNotRunning = true;
                    Debug.Print("[ActiveWindow] AppleScript -600 / application not running (grant Automation access in System Settings → Privacy & Security → Automation if needed).");
                }
                return string.Empty;
            }

            throw new Exception($"AppleScript error: {error}");
        }
        catch (Exception ex)
        {
            Debug.Print($"ExecuteAppleScript failed: {ex.Message}");
            return "Unknown";
        }
    }
}