using System.Runtime.InteropServices;

namespace Nudge.Notifications;

/// <summary>
/// Displays a topmost modal dialog that appears on top of all windows,
/// including fullscreen applications. Requires user acknowledgment to dismiss.
/// Uses Win32 SetWindowPos for reliable topmost behavior.
/// </summary>
public class ModalWarning
{
    // Win32 constants and P/Invoke declarations
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private const uint ASFW_ANY = unchecked((uint)-1);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Shows a modal warning dialog that appears on top of everything.
    /// This method blocks until the user dismisses the dialog.
    /// Must be called from a UI thread or marshalled appropriately.
    /// </summary>
    /// <param name="appName">The name of the tracked app.</param>
    /// <param name="message">The warning message to display.</param>
    /// <param name="accumulatedMinutes">Total minutes played today.</param>
    /// <param name="minutesUntilClose">Minutes until auto-close, or null if not applicable.</param>
    public void ShowWarning(string appName, string message, double accumulatedMinutes, double? minutesUntilClose)
    {
        // Run on a dedicated STA thread since we may be called from a timer thread
        var thread = new Thread(() =>
        {
            var form = CreateWarningForm(appName, message, accumulatedMinutes, minutesUntilClose);
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private Form CreateWarningForm(string appName, string message, double accumulatedMinutes, double? minutesUntilClose)
    {
        var form = new Form
        {
            Text = $"Nudge - {appName}",
            Width = 450,
            Height = 280,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            ShowInTaskbar = true
        };

        // Aggressively force the window to the foreground.
        // Windows restricts SetForegroundWindow, so we use multiple techniques:
        // 1. AttachThreadInput to the foreground thread to bypass the restriction
        // 2. SetWindowPos with HWND_TOPMOST
        // 3. ShowWindow + BringWindowToTop + SetForegroundWindow
        // 4. A short timer to re-assert topmost in case the OS fights back
        form.Load += (s, e) =>
        {
            ForceBringToFront(form.Handle);

            // Re-assert after a short delay in case the OS demoted us
            var reassertTimer = new System.Windows.Forms.Timer { Interval = 300 };
            int ticks = 0;
            reassertTimer.Tick += (ts, te) =>
            {
                ForceBringToFront(form.Handle);
                ticks++;
                if (ticks >= 5) // Stop after ~1.5 seconds
                    reassertTimer.Stop();
            };
            reassertTimer.Start();
        };

        // App name label
        var titleLabel = new Label
        {
            Text = $"  {appName}",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 180, 50),
            Dock = DockStyle.Top,
            Height = 45,
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Warning message
        var messageLabel = new Label
        {
            Text = message,
            Font = new Font("Segoe UI", 11),
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(10, 5, 10, 5),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Time info
        var timeText = $"  Total time today: {FormatMinutes(accumulatedMinutes)}";
        if (minutesUntilClose.HasValue)
        {
            timeText += $"\n  Auto-close in: {FormatMinutes(minutesUntilClose.Value)}";
        }

        var timeLabel = new Label
        {
            Text = timeText,
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(180, 180, 180),
            Dock = DockStyle.Top,
            Height = 50,
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Acknowledge button
        var button = new Button
        {
            Text = "I understand",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Width = 150,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 180, 50),
            ForeColor = Color.Black,
            Anchor = AnchorStyles.Bottom
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (s, e) => form.Close();

        // Center the button
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60
        };
        buttonPanel.Controls.Add(button);
        buttonPanel.Layout += (s, e) =>
        {
            button.Left = (buttonPanel.Width - button.Width) / 2;
            button.Top = (buttonPanel.Height - button.Height) / 2;
        };

        form.Controls.Add(buttonPanel);
        form.Controls.Add(timeLabel);
        form.Controls.Add(messageLabel);
        form.Controls.Add(titleLabel);

        return form;
    }

    /// <summary>
    /// Uses multiple Win32 techniques to force a window to the foreground,
    /// bypassing Windows' focus-stealing prevention.
    /// </summary>
    private static void ForceBringToFront(IntPtr hWnd)
    {
        try
        {
            // Allow our process to set foreground window
            AllowSetForegroundWindow(ASFW_ANY);

            // Attach to the foreground thread's input to gain focus privileges
            var foregroundWnd = GetForegroundWindow();
            uint foregroundThreadId = GetWindowThreadProcessId(foregroundWnd, out _);
            uint currentThreadId = GetCurrentThreadId();

            bool attached = false;
            if (foregroundThreadId != currentThreadId)
            {
                attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            try
            {
                // Force the window visible and to the top
                ShowWindow(hWnd, SW_RESTORE);
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                ShowWindow(hWnd, SW_SHOW);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }
        }
        catch
        {
            // Last resort fallback -- at minimum keep it topmost
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }
    }

    private static string FormatMinutes(double minutes)
    {
        if (minutes < 1)
            return "less than 1 minute";

        var hours = (int)(minutes / 60);
        var mins = (int)(minutes % 60);

        if (hours > 0)
            return $"{hours}h {mins}m";

        return $"{mins}m";
    }
}
