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
    /// Optional list of sources (processes) that contribute to this app's activity.
    /// When present and non-empty, supersedes <see cref="ProcessNames"/> and <see cref="TrackingMode"/>.
    /// The app is considered active if ANY source is active. Time accumulates once per tick
    /// regardless of how many sources are active simultaneously (no double-counting).
    /// </summary>
    public List<AppSource>? Sources { get; set; }

    /// <summary>
    /// The full schedule configuration for this app.
    /// </summary>
    public AppSchedule Schedule { get; set; } = new();

    /// <summary>
    /// Returns true if this app uses multi-source tracking (has a non-empty Sources list).
    /// </summary>
    public bool HasSources => Sources is { Count: > 0 };

    /// <summary>
    /// Gets all process names across all sources, or falls back to <see cref="ProcessNames"/>.
    /// Useful for auto-close, which needs to kill all related processes.
    /// </summary>
    public IEnumerable<string> GetAllProcessNames()
    {
        if (HasSources)
        {
            return Sources!.Select(s => s.ProcessName).Distinct();
        }
        return ProcessNames;
    }
}
