using System.Diagnostics;
using Nudge.Config;
using Nudge.Logging;
using Nudge.Notifications;

namespace Nudge.Core;

/// <summary>
/// The main orchestrator that ties all Nudge components together.
/// Runs the monitoring loop on a timer, evaluates rules, and triggers actions.
/// </summary>
public class NudgeEngine : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly AppMonitor _appMonitor;
    private readonly TimeTracker _timeTracker;
    private readonly RuleEngine _ruleEngine;
    private readonly AppKiller _appKiller;
    private readonly ToastNotifier _toastNotifier;
    private readonly ModalWarning _modalWarning;
    private readonly UsageLogger _usageLogger;

    private System.Threading.Timer? _pollTimer;
    private bool _disposed;

    // Track running processes across ticks to detect start/stop
    private readonly Dictionary<string, Process?> _trackedProcesses = new();

    /// <summary>
    /// Provides current app time states for the tray icon status display.
    /// </summary>
    public Dictionary<string, AppTimeState> GetActiveStates()
    {
        var states = new Dictionary<string, AppTimeState>();
        foreach (var app in _configManager.Config.TrackedApps)
        {
            if (app.Enabled)
            {
                states[app.Name] = _timeTracker.GetState(app.Name);
            }
        }
        return states;
    }

    /// <summary>
    /// Raised when tracking state changes (for tray icon tooltip updates).
    /// string = active app name (null if none), double = accumulated minutes.
    /// </summary>
    public event Action<string?, double?>? ActiveAppChanged;

    public NudgeEngine(ConfigManager configManager, string logDirectory)
    {
        _configManager = configManager;
        _appMonitor = new AppMonitor();
        _timeTracker = new TimeTracker(
            Path.Combine(logDirectory, "state"),
            configManager.Config.GlobalSettings.DayBoundaryHour);
        _ruleEngine = new RuleEngine();
        _appKiller = new AppKiller();
        _toastNotifier = new ToastNotifier();
        _modalWarning = new ModalWarning();
        _usageLogger = new UsageLogger(logDirectory, configManager.Config.GlobalSettings.LogUsageData);

        // Re-create timer when config changes (polling interval may have changed)
        _configManager.ConfigReloaded += OnConfigReloaded;
    }

    /// <summary>
    /// Starts the monitoring loop.
    /// </summary>
    public void Start()
    {
        var interval = _configManager.Config.GlobalSettings.PollingIntervalMs;
        _pollTimer = new System.Threading.Timer(OnTick, null, 0, interval);
        System.Diagnostics.Debug.WriteLine($"[Nudge] Engine started. Polling every {interval}ms.");
    }

    /// <summary>
    /// Stops the monitoring loop and saves state.
    /// </summary>
    public void Stop()
    {
        _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _pollTimer?.Dispose();
        _pollTimer = null;
        _timeTracker.ForceSave();
        System.Diagnostics.Debug.WriteLine("[Nudge] Engine stopped.");
    }

    private void OnTick(object? state)
    {
        try
        {
            var config = _configManager.Config;
            var now = DateTime.Now;
            string? currentActiveApp = null;
            double? currentActiveMinutes = null;

            foreach (var app in config.TrackedApps)
            {
                if (!app.Enabled)
                    continue;

                ProcessApp(app, now, config.GlobalSettings, ref currentActiveApp, ref currentActiveMinutes);
            }

            // Notify tray icon of current state
            ActiveAppChanged?.Invoke(currentActiveApp, currentActiveMinutes);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nudge] Tick error: {ex.Message}");
        }
    }

    private void ProcessApp(TrackedApp app, DateTime now, GlobalSettings globalSettings,
        ref string? currentActiveApp, ref double? currentActiveMinutes)
    {
        // Find the running process
        var process = _appMonitor.FindRunningProcess(app.ProcessNames);
        var isActive = _appMonitor.IsAppActive(app, process);

        var tickInterval = TimeSpan.FromMilliseconds(globalSettings.PollingIntervalMs);
        var timeState = _timeTracker.GetState(app.Name);

        // Track process start/stop for logging
        var wasTracked = _trackedProcesses.TryGetValue(app.Name, out var previousProcess);
        var wasRunning = wasTracked && previousProcess != null && !previousProcess.HasExited;

        if (isActive)
        {
            // Log session start
            if (!wasRunning || timeState.SessionStartUtc == null)
            {
                _usageLogger.LogEvent(app.Name, "session_start");
            }

            _timeTracker.RecordActiveTick(app.Name, tickInterval);
            currentActiveApp = app.Name;
            currentActiveMinutes = timeState.AccumulatedMinutes;

            // Resolve the applicable schedule for today
            var schedule = _ruleEngine.ResolveSchedule(app, now, globalSettings.DayBoundaryHour);

            // Check for pending warnings
            var pendingWarnings = _ruleEngine.GetPendingWarnings(
                schedule, timeState.AccumulatedMinutes, timeState.FiredMilestoneMinutes);

            foreach (var warning in pendingWarnings)
            {
                FireWarning(app, warning, timeState, schedule);
                _timeTracker.MarkMilestoneFired(app.Name, warning.AfterMinutes);
            }

            // Check pre-close warning
            if (_ruleEngine.ShouldSendPreCloseWarning(
                    schedule, timeState.AccumulatedMinutes, timeState.PreCloseWarningSent))
            {
                var minutesLeft = _ruleEngine.GetMinutesUntilAutoClose(schedule, timeState.AccumulatedMinutes);
                _toastNotifier.ShowPreCloseWarning(app.Name, (int)Math.Ceiling(minutesLeft ?? 0));
                _timeTracker.MarkPreCloseWarningSent(app.Name);
                _usageLogger.LogEvent(app.Name, "pre_close_warning",
                    $"Minutes remaining: {minutesLeft:F1}");
            }

            // Check auto-close
            if (_ruleEngine.ShouldAutoClose(schedule, timeState.AccumulatedMinutes))
            {
                var graceful = schedule.AutoClose?.GracefulClose ?? true;
                _appKiller.CloseProcesses(app.ProcessNames, graceful);
                _toastNotifier.ShowAutoCloseNotification(app.Name);
                _usageLogger.LogEvent(app.Name, "auto_close",
                    $"After {timeState.AccumulatedMinutes:F1} minutes");
                _timeTracker.RecordInactiveTick(app.Name);
            }
        }
        else
        {
            // Log session end
            if (wasRunning && timeState.SessionStartUtc != null)
            {
                _usageLogger.LogEvent(app.Name, "session_end",
                    $"Duration: {timeState.AccumulatedMinutes:F1} minutes");
            }

            _timeTracker.RecordInactiveTick(app.Name);
        }

        // Update tracked process reference
        if (previousProcess != null && previousProcess != process)
        {
            previousProcess.Dispose();
        }
        _trackedProcesses[app.Name] = process;
    }

    private void FireWarning(TrackedApp app, WarningMilestone warning,
        AppTimeState timeState, DaySchedule schedule)
    {
        var minutesUntilClose = _ruleEngine.GetMinutesUntilAutoClose(schedule, timeState.AccumulatedMinutes);

        switch (warning.Type.ToLowerInvariant())
        {
            case "modal":
                _modalWarning.ShowWarning(app.Name, warning.Message,
                    timeState.AccumulatedMinutes, minutesUntilClose);
                break;

            case "toast":
            default:
                // Use a stable tag per app+milestone so duplicate toasts replace each other
                _toastNotifier.ShowToast($"Nudge - {app.Name}", warning.Message,
                    tag: $"nudge_{app.Name}_{warning.AfterMinutes}min");
                break;
        }

        _usageLogger.LogEvent(app.Name, $"warning_{warning.Type}",
            $"Milestone: {warning.AfterMinutes}min - {warning.Message}");

        System.Diagnostics.Debug.WriteLine(
            $"[Nudge] Warning fired for {app.Name}: {warning.Message} ({warning.Type})");
    }

    private void OnConfigReloaded(object? sender, NudgeConfig newConfig)
    {
        // Restart the timer with the new polling interval
        var interval = newConfig.GlobalSettings.PollingIntervalMs;
        _pollTimer?.Change(0, interval);
        System.Diagnostics.Debug.WriteLine($"[Nudge] Config reloaded. Polling interval: {interval}ms.");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _configManager.ConfigReloaded -= OnConfigReloaded;

            foreach (var process in _trackedProcesses.Values)
            {
                process?.Dispose();
            }
            _trackedProcesses.Clear();

            _disposed = true;
        }
    }
}
