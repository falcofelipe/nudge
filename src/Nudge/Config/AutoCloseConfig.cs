namespace Nudge.Config;

/// <summary>
/// Configuration for automatically closing a tracked app after a time limit.
/// </summary>
public class AutoCloseConfig
{
    /// <summary>
    /// Whether auto-close is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Minutes of usage after which the app will be force-closed.
    /// </summary>
    public int AfterMinutes { get; set; }

    /// <summary>
    /// Minutes before the auto-close to show a final warning. 
    /// Set to 0 or null to close immediately without warning.
    /// </summary>
    public int? PreCloseWarningMinutes { get; set; }

    /// <summary>
    /// Whether to attempt a graceful close (CloseMainWindow) before force-killing.
    /// </summary>
    public bool GracefulClose { get; set; } = true;
}
