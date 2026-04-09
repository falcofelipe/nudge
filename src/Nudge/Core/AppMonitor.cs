using System.Diagnostics;
using System.Runtime.InteropServices;
using Nudge.Config;

namespace Nudge.Core;

/// <summary>
/// Monitors running processes and foreground window state to determine
/// which tracked apps are currently active. Uses Win32 APIs for foreground detection.
/// </summary>
public class AppMonitor
{
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
}
