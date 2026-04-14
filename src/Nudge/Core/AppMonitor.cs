using System.Diagnostics;
using System.Runtime.InteropServices;
using Nudge.Config;

namespace Nudge.Core;

/// <summary>
/// Monitors running processes and foreground window state to determine
/// which tracked apps are currently active. Uses Win32 APIs for foreground detection.
/// Supports "browser-tab" sources via an optional ChromeTabMonitor reference.
/// </summary>
public class AppMonitor
{
    private ChromeTabMonitor? _chromeTabMonitor;

    /// <summary>
    /// Sets the ChromeTabMonitor instance used to evaluate "browser-tab" sources.
    /// Called by NudgeEngine after constructing the monitor.
    /// </summary>
    public void SetChromeTabMonitor(ChromeTabMonitor? monitor)
    {
        _chromeTabMonitor = monitor;
    }

    // Win32 P/Invoke for foreground window detection
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// Checks whether any process matching the given names is currently running.
    /// Returns the first matching process, or null if none are running.
    /// </summary>
    public Process? FindRunningProcess(IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    // Return the first match; dispose the rest
                    for (int i = 1; i < processes.Length; i++)
                        processes[i].Dispose();

                    return processes[0];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Nudge] Error checking process '{name}': {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a process with the given ID currently has the foreground window.
    /// </summary>
    public bool IsProcessInForeground(int processId)
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(hwnd, out uint foregroundPid);
            return foregroundPid == (uint)processId;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nudge] Error checking foreground: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Determines whether a tracked app should be considered "active" based on its tracking mode.
    /// </summary>
    /// <param name="app">The tracked app configuration.</param>
    /// <param name="runningProcess">The running process (if any) matching the app's process names.</param>
    /// <returns>True if the app is considered active according to its tracking mode.</returns>
    public bool IsAppActive(TrackedApp app, Process? runningProcess)
    {
        if (runningProcess == null || runningProcess.HasExited)
            return false;

        return app.TrackingMode.ToLowerInvariant() switch
        {
            "process" => true, // If the process is running, it's active
            "foreground" => IsProcessInForeground(runningProcess.Id),
            _ => IsProcessInForeground(runningProcess.Id) // Default to foreground
        };
    }

    /// <summary>
    /// Checks if any source in a multi-source tracked app is currently active.
    /// Each source is checked independently against its own tracking mode.
    /// Returns true if at least one source is active.
    /// </summary>
    public bool IsAnySourceActive(TrackedApp app)
    {
        if (app.Sources == null || app.Sources.Count == 0)
            return false;

        foreach (var source in app.Sources)
        {
            if (IsSourceActive(source))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether a single source is active based on its process name and tracking mode.
    /// Supports "process", "foreground", and "browser-tab" modes.
    /// </summary>
    private bool IsSourceActive(AppSource source)
    {
        var trackingMode = source.TrackingMode.ToLowerInvariant();

        // Browser-tab mode: activity is determined entirely by WebSocket messages
        // from the Chrome extension -- no process detection needed.
        if (trackingMode == "browser-tab")
        {
            if (_chromeTabMonitor == null || source.TabPatterns == null || source.TabPatterns.Count == 0)
                return false;

            return _chromeTabMonitor.IsTabMatchActive(source.TabPatterns);
        }

        // Process/foreground modes: check the actual OS process
        try
        {
            var processes = Process.GetProcessesByName(source.ProcessName);
            if (processes.Length == 0)
                return false;

            try
            {
                if (trackingMode == "process")
                {
                    // Process mode: active if any matching process is running
                    return true;
                }

                // Foreground mode: active if any matching process has the foreground window
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited && IsProcessInForeground(process.Id))
                            return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Nudge] Error checking foreground for source '{source.ProcessName}': {ex.Message}");
                    }
                }

                return false;
            }
            finally
            {
                foreach (var p in processes)
                    p.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Nudge] Error checking source '{source.ProcessName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if any source in a multi-source tracked app has at least one running process.
    /// Used for detecting whether the app has any running processes (for session lifecycle tracking).
    /// </summary>
    public bool IsAnySourceProcessRunning(TrackedApp app)
    {
        if (app.Sources == null || app.Sources.Count == 0)
            return false;

        foreach (var source in app.Sources)
        {
            try
            {
                var processes = Process.GetProcessesByName(source.ProcessName);
                try
                {
                    if (processes.Length > 0)
                        return true;
                }
                finally
                {
                    foreach (var p in processes)
                        p.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Nudge] Error checking source process '{source.ProcessName}': {ex.Message}");
            }
        }

        return false;
    }
}
