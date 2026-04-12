using Nudge.Config;
using Nudge.Core;

namespace Nudge.UI;

/// <summary>
/// Manages the system tray icon and its context menu.
/// Provides quick access to status, config, and exit.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ConfigManager _configManager;
    private readonly Func<Dictionary<string, AppTimeState>> _getActiveStates;
    private bool _disposed;

    /// <summary>
    /// Raised when the user requests to exit the application.
    /// </summary>
    public event EventHandler? ExitRequested;

    public TrayIcon(ConfigManager configManager, Func<Dictionary<string, AppTimeState>> getActiveStates)
    {
        _configManager = configManager;
        _getActiveStates = getActiveStates;

        _notifyIcon = new NotifyIcon
        {
            Text = "Nudge - App Time Tracker",
            Icon = CreateDefaultIcon(),
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _notifyIcon.DoubleClick += (s, e) => ShowStatus();

        // Show a brief balloon tip so the user knows the app has started
        _notifyIcon.BalloonTipTitle = "Nudge";
        _notifyIcon.BalloonTipText = "Nudge is now running in the background.\nRight-click the tray icon for options.";
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3000);
    }

    /// <summary>
    /// Updates the tray icon tooltip with current tracking info.
    /// </summary>
    public void UpdateTooltip(string? activeApp, double? minutes)
    {
        try
        {
            var text = "Nudge - App Time Tracker";
            if (activeApp != null && minutes.HasValue)
            {
                var timeStr = FormatMinutes(minutes.Value);
                text = $"Nudge | {activeApp}: {timeStr}";
            }

            // NotifyIcon.Text has a 127-char limit
            _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
        }
        catch
        {
            // Ignore tooltip update errors
        }
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        var statusItem = new ToolStripMenuItem("Status", null, (s, e) => ShowStatus());
        var configItem = new ToolStripMenuItem("Open Config", null, (s, e) => OpenConfig());
        var configFolderItem = new ToolStripMenuItem("Open Config Folder", null, (s, e) => OpenConfigFolder());

        var autoStartItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = _configManager.Config.GlobalSettings.AutoStart
        };
        autoStartItem.CheckedChanged += (s, e) => HandleAutoStartToggle(autoStartItem);

        var separator = new ToolStripSeparator();
        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => HandleExit());

        menu.Items.AddRange(new ToolStripItem[]
        {
            statusItem, configItem, configFolderItem, autoStartItem, separator, exitItem
        });
        return menu;
    }

    private void HandleAutoStartToggle(ToolStripMenuItem menuItem)
    {
        var enabled = menuItem.Checked;
        _configManager.Config.GlobalSettings.AutoStart = enabled;
        _configManager.Save();
        AutoStartManager.SyncRegistryKey(enabled);

        if (enabled && !AutoStartManager.IsPublishedExe)
        {
            _notifyIcon.BalloonTipTitle = "Nudge";
            _notifyIcon.BalloonTipText = "Auto-start is saved in config but won't register until running as a published exe.";
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
            _notifyIcon.ShowBalloonTip(3000);
        }
    }

    private void ShowStatus()
    {
        var states = _getActiveStates();
        var config = _configManager.Config;

        var lines = new List<string> { "=== Nudge Status ===", "" };

        if (config.TrackedApps.Count == 0)
        {
            lines.Add("No apps configured for tracking.");
        }
        else
        {
            foreach (var app in config.TrackedApps)
            {
                var enabledStr = app.Enabled ? "ON" : "OFF";
                var line = $"  {app.Name} [{enabledStr}]";

                if (states.TryGetValue(app.Name, out var state))
                {
                    var timeStr = FormatMinutes(state.AccumulatedMinutes);
                    var activeStr = state.SessionStartUtc != null ? " (ACTIVE)" : "";
                    line += $" - {timeStr}{activeStr}";
                }
                else
                {
                    line += " - No usage today";
                }

                lines.Add(line);
            }
        }

        lines.Add("");
        lines.Add($"Day boundary: {config.GlobalSettings.DayBoundaryHour}:00 AM");
        lines.Add($"Polling interval: {config.GlobalSettings.PollingIntervalMs}ms");

        MessageBox.Show(
            string.Join("\n", lines),
            "Nudge - Status",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenConfig()
    {
        try
        {
            var configPath = _configManager.GetConfigPath();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open config file:\n{ex.Message}",
                "Nudge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenConfigFolder()
    {
        try
        {
            var configDir = Path.GetDirectoryName(_configManager.GetConfigPath())!;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = configDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open config folder:\n{ex.Message}",
                "Nudge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void HandleExit()
    {
        if (_configManager.Config.GlobalSettings.RequireExitConfirmation)
        {
            var result = MessageBox.Show(
                "Are you sure you want to exit Nudge?\n\n" +
                "Your apps will no longer be monitored and time limits won't be enforced.",
                "Nudge - Exit Confirmation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2); // Default to "No"

            if (result != DialogResult.Yes)
                return;
        }

        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a simple colored icon programmatically (no .ico file needed).
    /// </summary>
    private static Icon CreateDefaultIcon()
    {
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);

        // Dark background circle
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);

        // Orange "N" letter
        using var font = new Font("Segoe UI", 18, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(255, 180, 50));
        var textSize = g.MeasureString("N", font);
        var x = (32 - textSize.Width) / 2;
        var y = (32 - textSize.Height) / 2;
        g.DrawString("N", font, textBrush, x, y);

        var icon = Icon.FromHandle(bitmap.GetHicon());
        return icon;
    }

    private static string FormatMinutes(double minutes)
    {
        if (minutes < 1)
            return "< 1m";

        var hours = (int)(minutes / 60);
        var mins = (int)(minutes % 60);

        if (hours > 0)
            return $"{hours}h {mins}m";

        return $"{mins}m";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _disposed = true;
        }
    }
}
