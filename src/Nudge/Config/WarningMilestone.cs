namespace Nudge.Config;

/// <summary>
/// Defines a warning that fires after a certain amount of usage time.
/// </summary>
public class WarningMilestone
{
    /// <summary>
    /// Minutes of usage after which this warning fires.
    /// </summary>
    public int AfterMinutes { get; set; }

    /// <summary>
    /// The type of notification: "toast" or "modal".
    /// </summary>
    public string Type { get; set; } = "toast";

    /// <summary>
    /// The message displayed to the user.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
