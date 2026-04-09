using System.Globalization;

namespace Nudge.Logging;

/// <summary>
/// Logs usage events to CSV files for future analytics.
/// One file per tracking day, stored in the logs directory.
/// </summary>
public class UsageLogger
{
    private readonly string _logDirectory;
    private readonly bool _enabled;

    public UsageLogger(string logDirectory, bool enabled)
    {
        _logDirectory = logDirectory;
        _enabled = enabled;

        if (_enabled && !Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    /// <summary>
    /// Logs an event (e.g., warning fired, auto-close triggered, session start/end).
    /// </summary>
    public void LogEvent(string appName, string eventType, string? details = null)
    {
        if (!_enabled) return;

        try
        {
            var now = DateTime.Now;
            var logFile = Path.Combine(_logDirectory, $"usage_{now:yyyy-MM-dd}.csv");

            // Write header if new file
            if (!File.Exists(logFile))
            {
                File.WriteAllText(logFile, "Timestamp,App,Event,Details\n");
            }

            var timestamp = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var escapedDetails = (details ?? "").Replace("\"", "\"\"");
            var line = $"{timestamp},{EscapeCsv(appName)},{EscapeCsv(eventType)},\"{escapedDetails}\"\n";

            File.AppendAllText(logFile, line);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nudge] Failed to log event: {ex.Message}");
        }
    }

    /// <summary>
    /// Logs a session summary (total time for an app on a given day).
    /// </summary>
    public void LogSessionSummary(string appName, double totalMinutes)
    {
        LogEvent(appName, "session_summary", $"Total minutes: {totalMinutes:F1}");
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
