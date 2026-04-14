using Microsoft.Toolkit.Uwp.Notifications;
using Nudge.Config;
using Nudge.Core;
using Nudge.Notifications;
using Nudge.UI;

namespace Nudge;

static class Program
{
    /// <summary>
    /// The main entry point for Nudge.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // Prevent multiple instances
        using var mutex = new Mutex(true, "NudgeAppTimeTracker", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "Nudge is already running.\nCheck your system tray.",
                "Nudge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Register a no-op handler for toast activation callbacks.
        // Without this, the UWP Notifications library's default COM activation
        // behavior can cause duplicate toasts when running as a standalone .exe.
        ToastNotificationManagerCompat.OnActivated += _ => { };

        ApplicationConfiguration.Initialize();

        // Resolve paths: use NUDGE_BASE_DIR env var if set, otherwise
        // walk up from the executable to find the project root (where config/ lives).
        // This avoids the config getting buried in bin/Debug/.
        var baseDir = Environment.GetEnvironmentVariable("NUDGE_BASE_DIR")
            ?? FindProjectRoot(AppDomain.CurrentDomain.BaseDirectory)
            ?? AppDomain.CurrentDomain.BaseDirectory;

        var configDir = Path.Combine(baseDir, "config");
        var configPath = Path.Combine(configDir, "config.json");
        var exampleConfigPath = Path.Combine(configDir, "config.example.json");
        var logDir = Path.Combine(baseDir, "logs");

        // Ensure directories exist
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(logDir);

        // If config.json doesn't exist but the example does, copy it as a starting point
        if (!File.Exists(configPath) && File.Exists(exampleConfigPath))
        {
            File.Copy(exampleConfigPath, configPath);
        }

        // Initialize components
        using var configManager = new ConfigManager(configPath);
        configManager.Load();

        // Sync auto-start registry key with config (no-op in dev mode)
        AutoStartManager.SyncRegistryKey(configManager.Config.GlobalSettings.AutoStart);

        using var engine = new NudgeEngine(configManager, logDir);

        using var trayIcon = new TrayIcon(configManager, () => engine.GetActiveStates());

        // Wire up tray icon tooltip updates
        engine.ActiveAppChanged += (appName, minutes) =>
        {
            trayIcon.UpdateTooltip(appName, minutes);
        };

        // Wire up exit
        trayIcon.ExitRequested += (s, e) =>
        {
            engine.Stop();
            Application.Exit();
        };

        // Start the engine
        engine.Start();

        // Run the application message loop (keeps the tray icon alive)
        Application.Run();

        // Cleanup on exit
        ToastNotifier.Cleanup();
    }

    /// <summary>
    /// Resolves the base directory for config and logs.
    /// 
    /// Priority:
    /// 1. NUDGE_BASE_DIR environment variable (already handled by caller).
    /// 2. Development mode: walk up from the executable looking for a parent
    ///    that contains both "config/" and "Nudge.sln" (the repo root).
    /// 3. Standalone/published mode: use the directory where the .exe lives.
    ///    This lets users place config/ next to the published executable.
    /// </summary>
    private static string? FindProjectRoot(string startDir)
    {
        // First try development mode: look for repo root with Nudge.sln
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "config"))
                && File.Exists(Path.Combine(dir.FullName, "Nudge.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        // Standalone/published mode: use the executable's own directory.
        // For single-file apps, Environment.ProcessPath points to the actual .exe,
        // while AppDomain.CurrentDomain.BaseDirectory may point to a temp extraction folder.
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir != null)
            return exeDir;

        return null;
    }
}
