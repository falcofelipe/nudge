using Microsoft.Toolkit.Uwp.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Nudge.Notifications;

/// <summary>
/// Sends Windows toast notifications. These appear in the notification center
/// and can show even when fullscreen apps are running (depending on Windows focus assist settings).
/// </summary>
public class ToastNotifier
{
    /// <summary>
    /// Shows a persistent toast notification with the given title and message.
    /// Uses the Alarm scenario and a dismiss button to ensure the toast stays
    /// on screen until the user interacts with it.
    /// </summary>
    /// <param name="title">The notification title (e.g., app name).</param>
    /// <param name="message">The notification body message.</param>
    /// <param name="isUrgent">If true, plays the default notification sound.</param>
    public void ShowToast(string title, string message, bool isUrgent = false)
    {
        try
        {
            // Build the toast XML manually to use the Alarm scenario,
            // which reliably persists on screen until dismissed.
            var toastXml = $@"
                <toast scenario='alarm' duration='long'>
                    <visual>
                        <binding template='ToastGeneric'>
                            <text>{EscapeXml(title)}</text>
                            <text>{EscapeXml(message)}</text>
                            <text placement='attribution'>Nudge - Time Tracker</text>
                        </binding>
                    </visual>
                    <audio silent='{(isUrgent ? "false" : "true")}' />
                    <actions>
                        <action content='Dismiss' arguments='dismiss' activationType='background' />
                    </actions>
                </toast>";

            var doc = new XmlDocument();
            doc.LoadXml(toastXml);

            var toast = new ToastNotification(doc)
            {
                ExpiresOnReboot = true,
                Tag = $"nudge_{DateTime.Now.Ticks}",
                Group = "nudge_warnings"
            };

            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            notifier.Show(toast);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nudge] Toast notification failed: {ex.Message}");
        }
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    /// <summary>
    /// Shows a pre-close warning toast with a countdown message.
    /// </summary>
    public void ShowPreCloseWarning(string appName, int minutesRemaining)
    {
        ShowToast(
            $"Nudge - {appName}",
            $"Auto-close in {minutesRemaining} minute{(minutesRemaining == 1 ? "" : "s")}! Save your progress.",
            isUrgent: true);
    }

    /// <summary>
    /// Shows a notification that the app was auto-closed.
    /// </summary>
    public void ShowAutoCloseNotification(string appName)
    {
        ShowToast(
            $"Nudge - {appName}",
            $"{appName} has been closed. Time limit reached for today.",
            isUrgent: false);
    }

    /// <summary>
    /// Cleans up any registered toast notification handlers on shutdown.
    /// </summary>
    public static void Cleanup()
    {
        try
        {
            ToastNotificationManagerCompat.Uninstall();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
