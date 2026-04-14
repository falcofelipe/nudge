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
    /// How to track this source: "process" (counts while process is alive),
    /// "foreground" (only counts while window is in the foreground),
    /// or "browser-tab" (tracks specific browser tab content via Chrome extension).
    /// </summary>
    public string TrackingMode { get; set; } = "foreground";

    /// <summary>
    /// Glob/wildcard patterns to match against browser tab titles and URLs.
    /// Only used when TrackingMode is "browser-tab". Supports * and ? wildcards.
    /// Example: ["*Tibia*", "*tibia.com*"]
    /// </summary>
    public List<string>? TabPatterns { get; set; }
}
