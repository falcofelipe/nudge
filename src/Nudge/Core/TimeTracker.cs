using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nudge.Core;

/// <summary>
/// Tracks accumulated usage time per app per "tracking day".
/// A tracking day resets at the configured day boundary hour (default 3 AM).
/// Persists state to disk so time survives app restarts.
/// </summary>
public class TimeTracker
{
    private readonly string _statePath;
    private readonly int _dayBoundaryHour;
    private Dictionary<string, AppTimeState> _appStates = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TimeTracker(string stateDirectory, int dayBoundaryHour = 3)
    {
        _dayBoundaryHour = dayBoundaryHour;
        _statePath = Path.Combine(stateDirectory, "tracking_state.json");
        LoadState();
    }

    /// <summary>
    /// Gets or creates the time state for a given app. Resets if the tracking day has changed.
    /// </summary>
    public AppTimeState GetState(string appName)
    {
        var currentDay = GetTrackingDay(DateTime.Now);

        if (_appStates.TryGetValue(appName, out var state))
        {
            // Check if we need to reset for a new tracking day
            if (state.TrackingDay != currentDay)
            {
                state = new AppTimeState
                {
                    AppName = appName,
                    TrackingDay = currentDay,
                    AccumulatedMinutes = 0,
                    FiredMilestoneMinutes = new HashSet<int>(),
                    PreCloseWarningSent = false,
                    SessionStartUtc = null,
                    LastPostLimitWarningMinutes = null
                };
                _appStates[appName] = state;
                SaveState();
            }

            return state;
        }

        // New app, create fresh state
        state = new AppTimeState
        {
            AppName = appName,
            TrackingDay = currentDay,
            AccumulatedMinutes = 0,
            FiredMilestoneMinutes = new HashSet<int>(),
            PreCloseWarningSent = false,
            SessionStartUtc = null
        };
        _appStates[appName] = state;
        return state;
    }

    /// <summary>
    /// Called on each tick when an app is active. Updates accumulated time.
    /// </summary>
    public void RecordActiveTick(string appName, TimeSpan tickInterval)
    {
        var state = GetState(appName);

        if (state.SessionStartUtc == null)
        {
            // Session just started
            state.SessionStartUtc = DateTime.UtcNow;
        }

        state.AccumulatedMinutes += tickInterval.TotalMinutes;
        // Persist periodically (every ~30 seconds to avoid excessive disk writes)
        if ((int)(state.AccumulatedMinutes * 60) % 30 == 0)
        {
            SaveState();
        }
    }

    /// <summary>
    /// Called when an app becomes inactive (process closed or lost foreground).
    /// </summary>
    public void RecordInactiveTick(string appName)
    {
        var state = GetState(appName);

        if (state.SessionStartUtc != null)
        {
            // Session ended
            state.SessionStartUtc = null;
            SaveState();
        }
    }

    /// <summary>
    /// Marks a warning milestone as fired so it doesn't repeat.
    /// </summary>
    public void MarkMilestoneFired(string appName, int afterMinutes)
    {
        var state = GetState(appName);
        state.FiredMilestoneMinutes.Add(afterMinutes);
        SaveState();
    }

    /// <summary>
    /// Marks the pre-close warning as sent.
    /// </summary>
    public void MarkPreCloseWarningSent(string appName)
    {
        var state = GetState(appName);
        state.PreCloseWarningSent = true;
        SaveState();
    }

    /// <summary>
    /// Updates the post-limit warning time to the current accumulated minutes.
    /// Called when a recurring post-limit warning is fired.
    /// </summary>
    public void UpdatePostLimitWarningTime(string appName, double accumulatedMinutes)
    {
        var state = GetState(appName);
        state.LastPostLimitWarningMinutes = accumulatedMinutes;
        SaveState();
    }

    /// <summary>
    /// Forces a save of the current state to disk.
    /// </summary>
    public void ForceSave()
    {
        SaveState();
    }

    /// <summary>
    /// Determines the "tracking day" for a given timestamp.
    /// Days reset at the configured boundary hour (e.g., 3 AM).
    /// So 2:30 AM on Tuesday is still "Monday" for tracking purposes.
    /// </summary>
    private string GetTrackingDay(DateTime dateTime)
    {
        var adjusted = dateTime.Hour < _dayBoundaryHour
            ? dateTime.Date.AddDays(-1)
            : dateTime.Date;
        return adjusted.ToString("yyyy-MM-dd");
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var json = File.ReadAllText(_statePath);
                var states = JsonSerializer.Deserialize<List<AppTimeState>>(json, JsonOptions);
                _appStates = states?.ToDictionary(s => s.AppName, s => s) ?? new();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nudge] Failed to load state: {ex.Message}");
            _appStates = new();
        }
    }

    private void SaveState()
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath)!;
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(_appStates.Values.ToList(), JsonOptions);
            File.WriteAllText(_statePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Nudge] Failed to save state: {ex.Message}");
        }
    }
}

/// <summary>
/// Represents the tracking state for a single app on a single tracking day.
/// </summary>
public class AppTimeState
{
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// The tracking day in yyyy-MM-dd format (adjusted for day boundary hour).
    /// </summary>
    public string TrackingDay { get; set; } = string.Empty;

    /// <summary>
    /// Total accumulated minutes of active usage today.
    /// </summary>
    public double AccumulatedMinutes { get; set; }

    /// <summary>
    /// Set of milestone AfterMinutes values that have already been fired today.
    /// Prevents duplicate notifications.
    /// </summary>
    public HashSet<int> FiredMilestoneMinutes { get; set; } = new();

    /// <summary>
    /// Whether the pre-close warning has been sent today.
    /// </summary>
    public bool PreCloseWarningSent { get; set; }

    /// <summary>
    /// When the current continuous session started (null if app is not currently active).
    /// </summary>
    public DateTime? SessionStartUtc { get; set; }

    /// <summary>
    /// The accumulated minutes value when the last post-limit recurring warning was shown.
    /// Null means no post-limit warning has been fired yet today.
    /// </summary>
    public double? LastPostLimitWarningMinutes { get; set; }
}
