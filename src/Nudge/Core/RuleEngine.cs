using Nudge.Config;

namespace Nudge.Core;

/// <summary>
/// Evaluates the current schedule for a tracked app and determines
/// which actions (warnings, auto-close) should be triggered based on accumulated time.
/// </summary>
public class RuleEngine
{
    /// <summary>
    /// Resolves which DaySchedule applies for a given app right now,
    /// considering special dates, day-of-week overrides, and weekend grouping.
    /// Priority: SpecialDate > DayOfWeek override > "weekend" override > Default.
    /// </summary>
    public DaySchedule ResolveSchedule(TrackedApp app, DateTime now, int dayBoundaryHour)
    {
        // Adjust for day boundary (e.g., 2 AM on Tuesday is still "Monday" for scheduling)
        var effectiveDate = now.Hour < dayBoundaryHour
            ? now.Date.AddDays(-1)
            : now.Date;

        var schedule = app.Schedule;

        // Check special dates first (highest priority)
        var todayString = effectiveDate.ToString("yyyy-MM-dd");
        var specialDate = schedule.SpecialDates
            .FirstOrDefault(sd => sd.Date == todayString);

        if (specialDate != null)
        {
            return MergeWithDefault(schedule.Default, specialDate.Schedule);
        }

        // Check day-of-week overrides
        var dayName = effectiveDate.DayOfWeek.ToString().ToLowerInvariant();
        if (schedule.Overrides.TryGetValue(dayName, out var dayOverride))
        {
            return MergeWithDefault(schedule.Default, dayOverride);
        }

        // Check "weekend" group override (applies to Saturday and Sunday)
        if (dayName is "saturday" or "sunday"
            && schedule.Overrides.TryGetValue("weekend", out var weekendOverride))
        {
            return MergeWithDefault(schedule.Default, weekendOverride);
        }

        // Fall back to default
        return schedule.Default;
    }

    /// <summary>
    /// Gets warning milestones that should fire based on current accumulated time
    /// and which milestones have already been fired.
    /// </summary>
    public List<WarningMilestone> GetPendingWarnings(
        DaySchedule schedule,
        double accumulatedMinutes,
        HashSet<int> firedMilestoneMinutes)
    {
        return schedule.WarningMilestones
            .Where(m => accumulatedMinutes >= m.AfterMinutes && !firedMilestoneMinutes.Contains(m.AfterMinutes))
            .OrderBy(m => m.AfterMinutes)
            .ToList();
    }

    /// <summary>
    /// Determines if the pre-close warning should be sent.
    /// Returns true if auto-close is enabled, has a pre-close warning configured,
    /// and the accumulated time has reached the warning threshold.
    /// </summary>
    public bool ShouldSendPreCloseWarning(
        DaySchedule schedule,
        double accumulatedMinutes,
        bool preCloseWarningSent)
    {
        var autoClose = schedule.AutoClose;
        if (autoClose == null || !autoClose.Enabled || preCloseWarningSent)
            return false;

        if (autoClose.PreCloseWarningMinutes == null || autoClose.PreCloseWarningMinutes <= 0)
            return false;

        var warningThreshold = autoClose.AfterMinutes - autoClose.PreCloseWarningMinutes.Value;
        return accumulatedMinutes >= warningThreshold;
    }

    /// <summary>
    /// Determines if the app should be auto-closed now.
    /// </summary>
    public bool ShouldAutoClose(DaySchedule schedule, double accumulatedMinutes)
    {
        var autoClose = schedule.AutoClose;
        if (autoClose == null || !autoClose.Enabled)
            return false;

        return accumulatedMinutes >= autoClose.AfterMinutes;
    }

    /// <summary>
    /// Determines if a post-limit recurring warning should fire.
    /// Returns true when: all milestones have been fired, auto-close is disabled/null,
    /// postLimitRepeatIntervalMinutes is set and > 0, and enough time has passed since
    /// the last post-limit warning (or the last milestone, whichever was more recent).
    /// </summary>
    public bool ShouldFirePostLimitWarning(
        DaySchedule schedule,
        double accumulatedMinutes,
        HashSet<int> firedMilestoneMinutes,
        double? lastPostLimitWarningMinutes)
    {
        // Only activate when auto-close is disabled or not configured
        var autoClose = schedule.AutoClose;
        if (autoClose != null && autoClose.Enabled)
            return false;

        // Must have a positive repeat interval configured
        var interval = schedule.PostLimitRepeatIntervalMinutes;
        if (interval == null || interval <= 0)
            return false;

        // Must have milestones to check against
        if (schedule.WarningMilestones.Count == 0)
            return false;

        // All milestones must have been fired
        var allMilestoneMinutes = schedule.WarningMilestones.Select(m => m.AfterMinutes).ToHashSet();
        if (!allMilestoneMinutes.IsSubsetOf(firedMilestoneMinutes))
            return false;

        // Determine the anchor point: the last post-limit warning time,
        // or the last milestone's AfterMinutes if no post-limit warning has fired yet
        var lastMilestoneMinutes = schedule.WarningMilestones.Max(m => m.AfterMinutes);
        var anchor = lastPostLimitWarningMinutes ?? lastMilestoneMinutes;

        return accumulatedMinutes >= anchor + interval.Value;
    }

    /// <summary>
    /// Gets the last (highest AfterMinutes) warning milestone from a schedule.
    /// Returns null if there are no milestones.
    /// </summary>
    public WarningMilestone? GetLastMilestone(DaySchedule schedule)
    {
        if (schedule.WarningMilestones.Count == 0)
            return null;

        return schedule.WarningMilestones.OrderByDescending(m => m.AfterMinutes).First();
    }

    /// <summary>
    /// Returns the remaining minutes before auto-close, or null if auto-close is not enabled.
    /// </summary>
    public double? GetMinutesUntilAutoClose(DaySchedule schedule, double accumulatedMinutes)
    {
        var autoClose = schedule.AutoClose;
        if (autoClose == null || !autoClose.Enabled)
            return null;

        return Math.Max(0, autoClose.AfterMinutes - accumulatedMinutes);
    }

    /// <summary>
    /// Merges an override schedule with the default. The override's non-null/non-empty
    /// fields take precedence; everything else falls back to default.
    /// </summary>
    private DaySchedule MergeWithDefault(DaySchedule defaultSchedule, DaySchedule overrideSchedule)
    {
        return new DaySchedule
        {
            WarningMilestones = overrideSchedule.WarningMilestones.Count > 0
                ? overrideSchedule.WarningMilestones
                : defaultSchedule.WarningMilestones,

            AutoClose = overrideSchedule.AutoClose ?? defaultSchedule.AutoClose,

            PostLimitRepeatIntervalMinutes = overrideSchedule.PostLimitRepeatIntervalMinutes
                ?? defaultSchedule.PostLimitRepeatIntervalMinutes
        };
    }
}
