namespace Nudge.Config;

/// <summary>
/// Configuration for a single application to be monitored.
/// </summary>
public class TrackedApp
{
    /// <summary>
    /// A friendly display name for the app (e.g., "Factorio").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// One or more process names to look for (without .exe).
    /// The first matching process is used for tracking. This allows
    /// specifying the main game process separately from launchers.
    /// </summary>
    public List<string> ProcessNames { get; set; } = new();

    /// <summary>
    /// How to track time: "process" (counts while process is alive) 
    /// or "foreground" (only counts while window is in the foreground).
    /// </summary>
    public string TrackingMode { get; set; } = "foreground";

    /// <summary>
    /// Whether this app is currently being tracked. Allows disabling without removing config.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The full schedule configuration for this app.
    /// </summary>
    public AppSchedule Schedule { get; set; } = new();
}
