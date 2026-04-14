namespace Nudge.Config;

/// <summary>
/// Represents a single source (process) that contributes to a tracked app's activity.
/// Multiple sources can be combined under one TrackedApp so they share a single timer
/// and schedule. Each source has its own process name and tracking mode.
/// </summary>
public class AppSource
{
    /// <summary>
    /// The process name to look for (without .exe), e.g., "chrome" or "factorio".
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// How to track this source: "process" (counts while process is alive)
    /// or "foreground" (only counts while window is in the foreground).
    /// </summary>
    public string TrackingMode { get; set; } = "foreground";
}
