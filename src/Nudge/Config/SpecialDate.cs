namespace Nudge.Config;

/// <summary>
/// A schedule override for a specific date (e.g., public holidays).
/// Takes precedence over day-of-week overrides.
/// </summary>
public class SpecialDate
{
    /// <summary>
    /// The date in yyyy-MM-dd format.
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// A human-readable label for this date (e.g., "Christmas").
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The schedule to use on this date.
    /// </summary>
    public DaySchedule Schedule { get; set; } = new();
}
