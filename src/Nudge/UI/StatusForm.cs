using Nudge.Config;
using Nudge.Core;

namespace Nudge.UI;

/// <summary>
/// A live-updating status window that shows all tracked apps and their current usage.
/// Refreshes every second via a timer. Only one instance is shown at a time.
/// </summary>
public class StatusForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly Func<Dictionary<string, AppTimeState>> _getActiveStates;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Panel _contentPanel;
    private readonly Label _footerLabel;

    // Keep track of per-app labels so we can update in-place without flicker
    private readonly List<AppRow> _appRows = new();

    private static StatusForm? _instance;

    /// <summary>
    /// Shows the status form. If already open, brings it to front.
    /// </summary>
    public static void ShowInstance(ConfigManager configManager,
        Func<Dictionary<string, AppTimeState>> getActiveStates)
    {
        if (_instance != null && !_instance.IsDisposed)
        {
            _instance.BringToFront();
            _instance.Activate();
            return;
        }

        _instance = new StatusForm(configManager, getActiveStates);
        _instance.Show();
    }

    private StatusForm(ConfigManager configManager,
        Func<Dictionary<string, AppTimeState>> getActiveStates)
    {
        _configManager = configManager;
        _getActiveStates = getActiveStates;

        // Form setup
        Text = "Nudge - Status";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);
        Font = new Font("Segoe UI", 10f);
        Size = new Size(420, 300);
        MinimumSize = new Size(380, 200);
        ShowInTaskbar = true;
        Icon = CreateFormIcon();

        // Header label
        var headerLabel = new Label
        {
            Text = "Nudge Status",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 180, 50),
            AutoSize = true,
            Location = new Point(16, 14)
        };
        Controls.Add(headerLabel);

        // Content panel for app rows (starts below header with breathing room)
        _contentPanel = new Panel
        {
            Location = new Point(16, 62),
            BackColor = Color.FromArgb(30, 30, 30)
        };
        Controls.Add(_contentPanel);

        // Footer label (day boundary, polling interval)
        _footerLabel = new Label
        {
            ForeColor = Color.FromArgb(140, 140, 140),
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft
        };
        Controls.Add(_footerLabel);

        // Handle resizing
        Resize += (s, e) => LayoutControls();
        LayoutControls();

        // Build initial content
        RebuildAppRows();
        RefreshData();

        // Refresh every second
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (s, e) => RefreshData();
        _refreshTimer.Start();
    }

    private const int ContentTop = 62;
    private const int FooterHeight = 44;
    private const int ContentBottom = 20; // gap between table bottom and footer
    private const int SidePadding = 16;
    private const int RowHeight = 40;

    private void LayoutControls()
    {
        _contentPanel.Size = new Size(
            ClientSize.Width - SidePadding * 2,
            ClientSize.Height - ContentTop - FooterHeight - ContentBottom);
        _footerLabel.Location = new Point(SidePadding, ClientSize.Height - FooterHeight);
        _footerLabel.Size = new Size(ClientSize.Width - SidePadding * 2, FooterHeight);
    }

    /// <summary>
    /// Rebuilds the list of app row controls. Called on first load and when
    /// the tracked apps list changes (config reload).
    /// </summary>
    private void RebuildAppRows()
    {
        _contentPanel.Controls.Clear();
        _appRows.Clear();

        var config = _configManager.Config;
        if (config.TrackedApps.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No apps configured for tracking.",
                ForeColor = Color.FromArgb(160, 160, 160),
                AutoSize = true,
                Location = new Point(0, 4)
            };
            _contentPanel.Controls.Add(emptyLabel);
            return;
        }

        // Add rows in reverse order because Dock.Top stacks from first-added at top
        var rows = new List<AppRow>();
        foreach (var app in config.TrackedApps)
        {
            rows.Add(new AppRow(app.Name));
        }

        // Dock.Top adds controls top-down in reverse insertion order,
        // so add them in reverse to preserve config order
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            rows[i].Panel.Dock = DockStyle.Top;
            _contentPanel.Controls.Add(rows[i].Panel);
        }
        _appRows.AddRange(rows);

        // Adjust form height to fit content (up to a max).
        // Calculate the client area needed, then convert to form size (adds title bar).
        var totalRowHeight = rows.Count * RowHeight;
        var desiredClientHeight = ContentTop + totalRowHeight + ContentBottom + FooterHeight;
        var titleBarHeight = Height - ClientSize.Height; // actual title bar + border
        var desiredHeight = desiredClientHeight + titleBarHeight;
        var screenHeight = Screen.PrimaryScreen?.WorkingArea.Height ?? 800;
        var maxHeight = (int)(screenHeight * 0.6);
        Height = Math.Min(Math.Max(desiredHeight, 200), maxHeight);
        LayoutControls();
    }

    /// <summary>
    /// Updates all app rows with current state data. Called every second.
    /// </summary>
    private void RefreshData()
    {
        try
        {
            var states = _getActiveStates();
            var config = _configManager.Config;

            // If the app count changed (config reloaded), rebuild rows
            if (_appRows.Count != config.TrackedApps.Count ||
                !AppNamesMatch(config.TrackedApps))
            {
                RebuildAppRows();
            }

            for (int i = 0; i < config.TrackedApps.Count && i < _appRows.Count; i++)
            {
                var app = config.TrackedApps[i];
                var row = _appRows[i];

                var enabled = app.Enabled;
                states.TryGetValue(app.Name, out var state);

                row.Update(enabled, state);
            }

            _footerLabel.Text =
                $"Day boundary: {config.GlobalSettings.DayBoundaryHour}:00 AM  |  " +
                $"Polling: {config.GlobalSettings.PollingIntervalMs}ms";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nudge] StatusForm refresh error: {ex.Message}");
        }
    }

    private bool AppNamesMatch(List<TrackedApp> trackedApps)
    {
        for (int i = 0; i < trackedApps.Count && i < _appRows.Count; i++)
        {
            if (_appRows[i].AppName != trackedApps[i].Name)
                return false;
        }
        return true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        _instance = null;
        base.OnFormClosing(e);
    }

    private static Icon CreateFormIcon()
    {
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);
        using var font = new Font("Segoe UI", 18, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(255, 180, 50));
        var textSize = g.MeasureString("N", font);
        g.DrawString("N", font, textBrush, (32 - textSize.Width) / 2, (32 - textSize.Height) / 2);
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>
    /// Represents a single app's row in the status display.
    /// Uses a TableLayoutPanel for reliable left/right layout that doesn't clip.
    /// </summary>
    private class AppRow
    {
        public string AppName { get; }
        public Panel Panel { get; }

        private readonly Label _indicatorLabel;
        private readonly Label _nameLabel;
        private readonly Label _timeLabel;

        public AppRow(string appName)
        {
            AppName = appName;

            Panel = new Panel
            {
                Height = RowHeight,
                BackColor = Color.FromArgb(40, 40, 40)
            };

            // Activity indicator dot (left-anchored)
            _indicatorLabel = new Label
            {
                Text = "\u25CF", // filled circle
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(80, 80, 80),
                AutoSize = true,
                Location = new Point(10, 10)
            };

            // App name (left-anchored, next to dot)
            _nameLabel = new Label
            {
                Text = appName,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 220, 220),
                AutoSize = true,
                Location = new Point(28, 8)
            };

            // Time display (right-anchored)
            _timeLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(200, 200, 200),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight
            };
            // Position from the right edge; will be anchored so it stays right on resize
            _timeLabel.Location = new Point(Panel.Width - _timeLabel.PreferredWidth - 12, 8);

            Panel.Controls.AddRange(new Control[] { _indicatorLabel, _nameLabel, _timeLabel });
        }

        public void Update(bool enabled, AppTimeState? state)
        {
            var isActive = state?.SessionStartUtc != null;

            // Indicator dot: green when active, dim gray when idle, muted red when disabled
            if (!enabled)
            {
                _indicatorLabel.ForeColor = Color.FromArgb(120, 60, 60);
                _nameLabel.ForeColor = Color.FromArgb(120, 120, 120);
            }
            else if (isActive)
            {
                _indicatorLabel.ForeColor = Color.FromArgb(80, 220, 80);
                _nameLabel.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else
            {
                _indicatorLabel.ForeColor = Color.FromArgb(80, 80, 80);
                _nameLabel.ForeColor = Color.FromArgb(200, 200, 200);
            }

            // Name label: append OFF for disabled apps
            _nameLabel.Text = enabled ? AppName : $"{AppName} [OFF]";
            if (!enabled)
                _nameLabel.ForeColor = Color.FromArgb(120, 120, 120);

            // Time display
            if (state != null && state.AccumulatedMinutes > 0)
            {
                var timeStr = FormatMinutes(state.AccumulatedMinutes);
                _timeLabel.Text = timeStr;
                _timeLabel.ForeColor = isActive
                    ? Color.FromArgb(80, 220, 80)
                    : Color.FromArgb(180, 180, 180);
            }
            else
            {
                _timeLabel.Text = enabled ? "No usage today" : "—";
                _timeLabel.ForeColor = Color.FromArgb(100, 100, 100);
            }

            // Keep the time label pinned to the right edge
            _timeLabel.Location = new Point(
                Panel.Width - _timeLabel.PreferredWidth - 12, 8);
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
    }
}
