using Microsoft.Win32;

namespace Nudge.Core;

/// <summary>
/// Manages the Windows auto-start registry entry for Nudge.
/// Uses HKCU\Software\Microsoft\Windows\CurrentVersion\Run to register/unregister
/// the app to start automatically when the user logs in.
/// </summary>
public static class AutoStartManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "Nudge";

    /// <summary>
    /// Returns true if the app is running as a published exe (not via dotnet.exe in dev mode).
    /// Auto-start should only be registered when running as a standalone executable.
    /// </summary>
    public static bool IsPublishedExe
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
                return false;

            var fileName = Path.GetFileName(processPath);
            return !fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Syncs the registry auto-start entry with the desired state.
    /// If enabled and running as a published exe, writes the registry value.
    /// If disabled, removes the registry value if it exists.
    /// In dev mode, this is a no-op regardless of the enabled parameter.
    /// </summary>
    public static void SyncRegistryKey(bool enabled)
    {
        if (!IsPublishedExe)
        {
            System.Diagnostics.Debug.WriteLine(
                "[Nudge] Auto-start registry sync skipped (running in dev mode via dotnet.exe).");
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            if (key == null)
                return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath!;
                key.SetValue(RegistryValueName, $"\"{exePath}\"");
                System.Diagnostics.Debug.WriteLine($"[Nudge] Auto-start registered: {exePath}");
            }
            else
            {
                if (key.GetValue(RegistryValueName) != null)
                {
                    key.DeleteValue(RegistryValueName, throwOnMissingValue: false);
                    System.Diagnostics.Debug.WriteLine("[Nudge] Auto-start registry entry removed.");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nudge] Failed to update auto-start registry: {ex.Message}");
        }
    }
}
