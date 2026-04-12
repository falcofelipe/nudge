namespace Nudge.Config;

/// <summary>
/// Global settings that apply across all tracked apps.
/// </summary>
public class GlobalSettings
{
    /// <summary>
    /// How often (in milliseconds) to poll for process and foreground changes.
    /// Lower values are more responsive but use more CPU. Default: 1000ms.
    /// </summary>
    public int PollingIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Default tracking mode for apps that don't specify one: "process" or "foreground".
    /// </summary>
    public string DefaultTrackingMode { get; set; } = "foreground";

    /// <summary>
    /// Whether to log usage data to disk for future analytics.
    /// </summary>
    public bool LogUsageData { get; set; } = true;

    /// <summary>
    /// The hour at which a new "day" begins for tracking purposes.
    /// Default: 3 (3:00 AM). Sessions that span midnight won't reset until this hour.
    /// </summary>
    public int DayBoundaryHour { get; set; } = 3;

    /// <summary>
    /// Whether to require confirmation before exiting Nudge via the tray icon.
    /// Adds friction against impulsive quitting.
    /// </summary>
    public bool RequireExitConfirmation { get; set; } = true;

    /// <summary>
    /// Whether Nudge should start automatically with Windows.
    /// Uses the HKCU\Software\Microsoft\Windows\CurrentVersion\Run registry key.
    /// Only takes effect when running as a published exe (ignored in dev mode).
    /// </summary>
    public bool AutoStart { get; set; } = false;
}
