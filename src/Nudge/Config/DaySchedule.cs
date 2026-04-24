namespace Nudge.Config;

/// <summary>
/// Defines warning milestones and auto-close rules for a specific time period
/// (default schedule, day-of-week override, or special date).
/// </summary>
public class DaySchedule
{
    /// <summary>
    /// Warning milestones for this schedule. Each fires once per session day.
    /// </summary>
    public List<WarningMilestone> WarningMilestones { get; set; } = new();

    /// <summary>
    /// Auto-close configuration for this schedule. Null means inherit from default.
    /// </summary>
    public AutoCloseConfig? AutoClose { get; set; }

    /// <summary>
    /// When all warning milestones have fired and auto-close is disabled,
    /// re-fire the last milestone's warning as a modal every N minutes.
    /// Null or 0 means no recurring warnings.
    /// </summary>
    public int? PostLimitRepeatIntervalMinutes { get; set; }
}
