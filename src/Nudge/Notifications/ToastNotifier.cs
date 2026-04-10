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
    /// <param name="tag">Stable tag for this toast. Toasts with the same tag replace each other
    /// instead of stacking. If null, a unique tag is generated.</param>
    public void ShowToast(string title, string message, bool isUrgent = false, string? tag = null)
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

            // Use a stable tag so duplicate toasts replace each other instead of stacking.
            // Tag has a max length of 64 chars and must not contain special characters.
            var toastTag = tag ?? $"nudge_{DateTime.Now.Ticks}";
            if (toastTag.Length > 64)
                toastTag = toastTag[..64];

            var toast = new ToastNotification(doc)
            {
                ExpiresOnReboot = true,
                Tag = toastTag,
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
    /// Sanitizes a string for use as a toast notification tag.
    /// Tags must be alphanumeric/underscores only, max 64 chars.
    /// </summary>
    private static string SanitizeTag(string input)
    {
        var sanitized = new string(input.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return sanitized.Length > 40 ? sanitized[..40] : sanitized;
    }

    /// <summary>
    /// Shows a pre-close warning toast with a countdown message.
    /// </summary>
    public void ShowPreCloseWarning(string appName, int minutesRemaining)
    {
        ShowToast(
            $"Nudge - {appName}",
            $"Auto-close in {minutesRemaining} minute{(minutesRemaining == 1 ? "" : "s")}! Save your progress.",
            isUrgent: true,
            tag: $"nudge_preclose_{SanitizeTag(appName)}");
    }

    /// <summary>
    /// Shows a notification that the app was auto-closed.
    /// </summary>
    public void ShowAutoCloseNotification(string appName)
    {
        ShowToast(
            $"Nudge - {appName}",
            $"{appName} has been closed. Time limit reached for today.",
            isUrgent: false,
            tag: $"nudge_closed_{SanitizeTag(appName)}");
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
