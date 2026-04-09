namespace Nudge.Config;

/// <summary>
/// Root configuration object for Nudge. Deserialized from config.json.
/// </summary>
public class NudgeConfig
{
    /// <summary>
    /// Global settings that apply across all tracked apps.
    /// </summary>
    public GlobalSettings GlobalSettings { get; set; } = new();

    /// <summary>
    /// List of applications to monitor and enforce time limits on.
    /// </summary>
    public List<TrackedApp> TrackedApps { get; set; } = new();
}
