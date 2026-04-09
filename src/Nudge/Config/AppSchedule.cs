namespace Nudge.Config;

/// <summary>
/// The full schedule configuration for a tracked app, including
/// default rules, day-of-week overrides, and special date overrides.
/// </summary>
public class AppSchedule
{
    /// <summary>
    /// The default schedule used when no override matches.
    /// </summary>
    public DaySchedule Default { get; set; } = new();

    /// <summary>
    /// Day-of-week overrides. Keys are lowercase day names (e.g., "monday", "saturday").
    /// Only the fields specified will override the default; unspecified fields inherit.
    /// </summary>
    public Dictionary<string, DaySchedule> Overrides { get; set; } = new();

    /// <summary>
    /// Special date overrides (e.g., holidays). These take priority over day-of-week overrides.
    /// </summary>
    public List<SpecialDate> SpecialDates { get; set; } = new();
}
