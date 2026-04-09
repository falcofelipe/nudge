using System.Diagnostics;

namespace Nudge.Core;

/// <summary>
/// Handles force-closing tracked app processes. Supports graceful close
/// (via CloseMainWindow) with fallback to Kill.
/// </summary>
public class AppKiller
{
    /// <summary>
    /// Attempts to close all processes matching the given names.
    /// </summary>
    /// <param name="processNames">Process names to close (without .exe).</param>
    /// <param name="graceful">If true, tries CloseMainWindow first before killing.</param>
    /// <param name="gracefulTimeoutMs">How long to wait for graceful close before force-killing.</param>
    public void CloseProcesses(IEnumerable<string> processNames, bool graceful = true, int gracefulTimeoutMs = 5000)
    {
        foreach (var name in processNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var process in processes)
                {
                    try
                    {
                        CloseProcess(process, graceful, gracefulTimeoutMs);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Nudge] Failed to close process '{name}' (PID {process.Id}): {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Nudge] Error finding process '{name}': {ex.Message}");
            }
        }
    }

    private void CloseProcess(Process process, bool graceful, int gracefulTimeoutMs)
    {
        if (process.HasExited)
            return;

        if (graceful)
        {
            // Try graceful close first
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                process.CloseMainWindow();

                // Wait for the process to exit gracefully
                if (process.WaitForExit(gracefulTimeoutMs))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Nudge] Process '{process.ProcessName}' closed gracefully.");
                    return;
                }
            }
        }

        // Force kill if still running
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            System.Diagnostics.Debug.WriteLine(
                $"[Nudge] Process '{process.ProcessName}' was force-killed.");
        }
    }
}
