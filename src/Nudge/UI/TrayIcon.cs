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
    private readonly Icon _defaultIcon;
    private readonly Icon _activeIcon;
    private bool _isShowingActiveIcon;
    private bool _disposed;

    /// <summary>
    /// Raised when the user requests to exit the application.
    /// </summary>
    public event EventHandler? ExitRequested;

    public TrayIcon(ConfigManager configManager, Func<Dictionary<string, AppTimeState>> getActiveStates)
    {
        _configManager = configManager;
        _getActiveStates = getActiveStates;

        _defaultIcon = CreateTrayIcon(Color.FromArgb(255, 180, 50)); // orange N
        _activeIcon = CreateTrayIcon(Color.FromArgb(80, 220, 80));   // green N

        _notifyIcon = new NotifyIcon
        {
            Text = "Nudge - App Time Tracker",
            Icon = _defaultIcon,
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
            var isActive = activeApp != null && minutes.HasValue;

            if (isActive)
            {
                var timeStr = FormatMinutes(minutes!.Value);
                text = $"Nudge | {activeApp}: {timeStr}";
            }

            // NotifyIcon.Text has a 127-char limit
            _notifyIcon.Text = text.Length > 127 ? text[..127] : text;

            // Swap tray icon: green when any tracked app is active, orange when idle
            if (isActive && !_isShowingActiveIcon)
            {
                _notifyIcon.Icon = _activeIcon;
                _isShowingActiveIcon = true;
            }
            else if (!isActive && _isShowingActiveIcon)
            {
                _notifyIcon.Icon = _defaultIcon;
                _isShowingActiveIcon = false;
            }
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
        StatusForm.ShowInstance(_configManager, _getActiveStates);
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
    /// Creates a tray icon with the "N" letter in the specified color.
    /// </summary>
    private static Icon CreateTrayIcon(Color letterColor)
    {
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);

        // Dark background circle
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);

        // Colored "N" letter
        using var font = new Font("Segoe UI", 18, FontStyle.Bold);
        using var textBrush = new SolidBrush(letterColor);
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
