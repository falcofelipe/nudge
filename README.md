# Nudge

A Windows system tray app that monitors app usage, sends time-based warnings, and can auto-close apps after configurable time limits. Built for ADHD self-management.

## Features

- **Process monitoring** - Track when specific apps are running
- **Two tracking modes** - Count time while the process is alive (`process` mode) or only when the window is focused (`foreground` mode), configurable per app
- **Warning milestones** - Toast notifications and/or modal dialogs at configurable time thresholds
- **Auto-close** - Automatically kill app processes after a time limit, with optional pre-close warning
- **Per-day scheduling** - Different rules for each day of the week
- **Special dates** - Override rules for holidays or specific dates
- **Configurable day boundary** - Day resets at 3 AM by default (not midnight) so late-night sessions count correctly
- **Hot-reload config** - Edit `config.json` and changes apply immediately, no restart needed
- **Usage logging** - CSV logs for tracking patterns over time
- **Exit friction** - Confirmation dialog before quitting to prevent impulsive disabling

## Requirements

- Windows 10 (build 19041+) or Windows 11
- .NET 8.0 SDK (for building) or .NET 8.0 Runtime (for running)

## Quick Start

### Build and Run

```powershell
# Clone/navigate to the project
cd app-time-tracker

# Build
dotnet build

# Run
dotnet run --project src/Nudge
```

### Publish as a standalone app (recommended)

Run the publish script to create a self-contained single-file `.exe`:

```powershell
.\publish.ps1
```

This creates a `publish/` folder with `Nudge.exe` and a copy of your `config/` folder. You can:

1. **Double-click `Nudge.exe`** to launch -- no terminal or .NET SDK needed
2. Move the entire `publish/` folder anywhere you like
3. Create a Desktop shortcut to `Nudge.exe` for quick access
4. Add a shortcut to your Startup folder (`shell:startup`) to auto-start with Windows

On launch, a balloon tip will appear confirming Nudge is running in the background.

You can also publish manually:

```powershell
dotnet publish src/Nudge -c Release -o ./publish
```

### First Run

On first launch, Nudge will:
1. Show a balloon notification confirming it's running in the background
2. Create a `config/config.json` file with example configuration (if one doesn't exist)
3. Appear in your system tray as an orange "N" icon
4. Start monitoring configured apps

The example config includes a **Notepad test** (enabled) and a disabled example game template. Open Notepad to verify warnings are working.

## Configuration

All configuration lives in `config/config.json` (relative to the executable). You can open it directly from the tray icon's right-click menu.

### Global Settings

```json
{
  "globalSettings": {
    "pollingIntervalMs": 1000,
    "defaultTrackingMode": "foreground",
    "logUsageData": true,
    "dayBoundaryHour": 3,
    "requireExitConfirmation": true
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `pollingIntervalMs` | int | `1000` | How often to check processes (ms). Lower = more responsive, higher = less CPU |
| `defaultTrackingMode` | string | `"foreground"` | Default tracking mode: `"process"` or `"foreground"` |
| `logUsageData` | bool | `true` | Whether to write usage events to CSV logs |
| `dayBoundaryHour` | int | `3` | Hour (0-23) when the tracking "day" resets. Default 3 = 3:00 AM |
| `requireExitConfirmation` | bool | `true` | Show confirmation dialog before exiting Nudge |

### Tracked Apps

Each tracked app has the following structure:

```json
{
  "name": "My Game",
  "processNames": ["mygame"],
  "trackingMode": "process",
  "enabled": true,
  "schedule": { ... }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Display name for notifications and status |
| `processNames` | string[] | Process names to look for (without `.exe`). First match is used |
| `trackingMode` | string | `"process"` (counts while alive) or `"foreground"` (only when focused) |
| `enabled` | bool | Toggle tracking without removing the config entry |
| `schedule` | object | Warning and auto-close rules (see below) |

#### Finding Process Names

To find the correct process name for an app:

1. Open Task Manager (Ctrl+Shift+Esc)
2. Go to the "Details" tab
3. Find your app and note the name (without `.exe`)

For example: `factorio.exe` -> use `"factorio"`.

### Schedule Configuration

Each app has a schedule with three layers (highest priority first):

1. **Special dates** - Specific dates like holidays
2. **Day-of-week overrides** - Per-day rules (e.g., relaxed weekend limits)
3. **Default** - Used when no override matches

```json
{
  "schedule": {
    "default": {
      "warningMilestones": [
        { "afterMinutes": 30, "type": "toast", "message": "30 minutes played" },
        { "afterMinutes": 60, "type": "toast", "message": "1 hour!" },
        { "afterMinutes": 90, "type": "modal", "message": "90 min - time to stop?" }
      ],
      "autoClose": {
        "enabled": true,
        "afterMinutes": 120,
        "preCloseWarningMinutes": 5,
        "gracefulClose": true
      }
    },
    "overrides": {
      "saturday": {
        "warningMilestones": [ ... ],
        "autoClose": { "enabled": false }
      }
    },
    "specialDates": [
      {
        "date": "2026-12-25",
        "label": "Christmas",
        "schedule": {
          "warningMilestones": [ ... ],
          "autoClose": { "enabled": false }
        }
      }
    ]
  }
}
```

#### Warning Milestones

| Field | Type | Description |
|-------|------|-------------|
| `afterMinutes` | int | Fire this warning after N minutes of accumulated usage today |
| `type` | string | `"toast"` (Windows notification) or `"modal"` (topmost dialog requiring acknowledgment) |
| `message` | string | The message shown to the user |

Each milestone fires **once per tracking day**. They reset at the configured day boundary hour.

#### Auto-Close

| Field | Type | Description |
|-------|------|-------------|
| `enabled` | bool | Whether auto-close is active |
| `afterMinutes` | int | Kill the process after this many minutes |
| `preCloseWarningMinutes` | int/null | Show a warning N minutes before closing. `null` or `0` = no warning |
| `gracefulClose` | bool | Try `CloseMainWindow()` before `Kill()`. Gives the app a chance to save |

#### Day-of-Week Overrides

Use lowercase day names as keys: `monday`, `tuesday`, `wednesday`, `thursday`, `friday`, `saturday`, `sunday`.

Override schedules are **merged** with the default:
- If the override specifies `warningMilestones`, those replace the default milestones
- If the override specifies `autoClose`, it replaces the default auto-close
- Unspecified fields inherit from the default

### Notification Types

| Type | Behavior | Best For |
|------|----------|----------|
| `toast` | Windows notification (bottom-right). Can be dismissed. May not show in exclusive fullscreen games. | Early/gentle warnings |
| `modal` | Topmost dialog with "I understand" button. Appears over fullscreen apps via Win32 API. | Serious warnings, close to time limit |

## System Tray

Right-click the orange "N" tray icon for:

- **Status** - Shows all tracked apps, their enabled state, and today's accumulated time
- **Open Config** - Opens `config.json` in your default editor
- **Open Config Folder** - Opens the config directory in Explorer
- **Exit** - Shuts down Nudge (with confirmation dialog if enabled)

Double-click the tray icon to open the status view.

The tray icon tooltip shows the currently active tracked app and its time.

## Usage Logs

When `logUsageData` is enabled, Nudge writes CSV files to the `logs/` directory:

```
logs/usage_2026-04-08.csv
```

Each file contains events like session starts, session ends, warnings fired, and auto-closes:

```csv
Timestamp,App,Event,Details
2026-04-08 14:30:00,Factorio,session_start,""
2026-04-08 15:00:00,Factorio,warning_toast,"Milestone: 30min - 30 minutes played"
2026-04-08 15:30:02,Factorio,session_end,"Duration: 60.1 minutes"
```

## Architecture

```
src/Nudge/
├── Program.cs              # Entry point, wires everything together
├── Core/
│   ├── NudgeEngine.cs      # Main orchestrator / monitoring loop
│   ├── AppMonitor.cs       # Process detection + foreground tracking (Win32)
│   ├── TimeTracker.cs      # Per-app time accumulation + state persistence
│   ├── RuleEngine.cs       # Schedule resolution + milestone evaluation
│   └── AppKiller.cs        # Process termination (graceful + force)
├── Config/
│   ├── NudgeConfig.cs      # Root config model
│   ├── GlobalSettings.cs   # Global settings model
│   ├── TrackedApp.cs       # Per-app config model
│   ├── AppSchedule.cs      # Schedule container (default + overrides)
│   ├── DaySchedule.cs      # Warning milestones + auto-close for a time period
│   ├── WarningMilestone.cs # Single warning definition
│   ├── AutoCloseConfig.cs  # Auto-close settings
│   ├── SpecialDate.cs      # Holiday/special date override
│   └── ConfigManager.cs    # JSON load/save/hot-reload with FileSystemWatcher
├── Notifications/
│   ├── ToastNotifier.cs    # Windows toast notifications
│   └── ModalWarning.cs     # Topmost WinForms dialog (Win32 P/Invoke)
├── UI/
│   └── TrayIcon.cs         # System tray icon + context menu
└── Logging/
    └── UsageLogger.cs      # CSV event logging
```

### Key Design Decisions

- **Single-process tray app**: Simple deployment, no Windows Service complexity
- **JSON config with hot-reload**: Edit and save, changes apply instantly via `FileSystemWatcher`
- **Win32 P/Invoke for foreground detection**: `GetForegroundWindow` + `GetWindowThreadProcessId` for accurate active-window tracking
- **Win32 `SetWindowPos(HWND_TOPMOST)` for modal warnings**: More reliable than WinForms `TopMost` property for appearing over fullscreen games
- **Separate namespaces for extensibility**: Core/Config/Notifications/UI/Logging are cleanly separated for future features
- **State persistence**: Tracking state is saved to `logs/state/tracking_state.json` so time survives Nudge restarts
- **Mutex for single instance**: Prevents running multiple copies accidentally

## Future Ideas

Detailed implementation plans are in `AGENTS.md` under "Future Plans". Priority features:

- **Windows auto-start** -- Registry Run key integration
- **Weekend grouping** -- Join "saturday"/"sunday" under single "weekend" key
- **Shared time pools** -- Multiple apps count time together
- **Separate counters** -- Same schedule, different time counters per app
- **Chrome tab tracking** -- Browser extension + WebSocket for tab content limits
- Settings UI, Pomodoro timer, URL blocking, usage reports, tray icon color changes
