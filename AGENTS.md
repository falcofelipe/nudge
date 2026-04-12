# Nudge - Agent Instructions

## Project Overview

Nudge is a Windows system tray app for ADHD self-management that monitors app usage, sends time-based warnings (toast/modal), and can auto-close apps after configurable limits. It runs as a single-process WinForms tray app with no console window.

CRITICAL: Always read `README.md` at the start of a session to understand the full app structure, configuration format, and design decisions before making changes.

## Tech Stack

- **Language**: C# 12 (.NET 8.0, `net8.0-windows10.0.19041.0`)
- **UI**: Windows Forms (system tray app, modal dialogs)
- **Notifications**: Microsoft.Toolkit.Uwp.Notifications 7.1.3
- **Serialization**: System.Text.Json 8.0.5 (camelCase naming policy)
- **Win32 Interop**: P/Invoke (`user32.dll`, `kernel32.dll`)
- **Platform**: Windows-only (Win32 APIs, WinForms)

## Architecture

```
src/Nudge/
├── Program.cs              # Entry point, mutex, base dir resolution, wires components
├── Core/                   # Engine, monitoring, time tracking, rules, app killing
├── Config/                 # JSON config models + hot-reload ConfigManager
├── Notifications/          # Toast (UWP toolkit) + Modal (Win32 topmost dialog)
├── UI/                     # System tray icon + context menu
└── Logging/                # CSV event logging
```

Key namespaces: `Nudge`, `Nudge.Core`, `Nudge.Config`, `Nudge.Notifications`, `Nudge.UI`, `Nudge.Logging`

## Build & Run Commands

**IMPORTANT - .NET SDK PATH issue**: This machine has two dotnet installs. The one in `C:\Program Files\dotnet\` is runtime-only (no SDK) and appears first in PATH, shadowing the actual SDK. The bare `dotnet` command will fail with "No .NET SDKs were found". Always use the full path to the SDK install instead:

```powershell
# The correct dotnet executable with SDK 8.0:
$dotnet = "C:\Users\falcof\AppData\Local\dotnet-sdk\dotnet.exe"

# Build
& $dotnet build

# Run in development
& $dotnet run --project src/Nudge

# Publish standalone exe (uses bare `dotnet` internally, so set DOTNET_ROOT first)
$env:DOTNET_ROOT = "C:\Users\falcof\AppData\Local\dotnet-sdk"
& $dotnet publish src/Nudge -c Release -o ./publish
# Then copy config: Copy-Item -Recurse -Force .\config .\publish\config

# Or use the publish script (same DOTNET_ROOT workaround needed):
$env:DOTNET_ROOT = "C:\Users\falcof\AppData\Local\dotnet-sdk"
.\publish.ps1
```

PATH cannot be permanently fixed (company-managed machine with limited access).

## Project Conventions

### Code Style
- File-scoped namespaces (e.g., `namespace Nudge.Core;`)
- Nullable reference types enabled (`#nullable enable` is implicit)
- Implicit usings enabled
- One class per file, filename matches class name
- PascalCase for public members, _camelCase for private fields
- Use `System.Text.Json` for all JSON serialization (never Newtonsoft)
- JSON config uses **camelCase** property naming (`JsonSerializerOptions` with `CamelCaseNamingPolicy`)

### Architecture Rules
- Keep namespaces cleanly separated: Core, Config, Notifications, UI, Logging
- New features should go in the appropriate namespace folder or get a new namespace if they represent a new concern
- Components communicate via events (e.g., `ActiveAppChanged`, `ConfigReloaded`, `ExitRequested`) -- avoid tight coupling
- The `NudgeEngine` is the central orchestrator; new monitoring features plug into its timer loop
- `ConfigManager` handles hot-reload -- new config fields must be added to the appropriate model class and the JSON schema
- All Win32 P/Invoke declarations should use `[LibraryImport]` or `[DllImport]` with clear comments explaining the Win32 function

### Config Model Pattern
When adding new configuration options:
1. Add the property to the appropriate model class in `Config/`
2. Use nullable types with sensible defaults for backward compatibility
3. Update `README.md` configuration tables
4. Test that hot-reload works with the new field

### Git Commit Messages
Follow [Conventional Commits](https://www.conventionalcommits.org/) with lowercase, no scope, no trailing period:
- `feat: add auto-start with Windows registry`
- `fix: prevent duplicate toast notifications in standalone mode`
- `refactor: extract schedule resolution into RuleEngine`
- `docs: update README with weekend grouping example`
- `chore: update publish script for new config structure`

Rules:
- **Type prefix is required**: `feat`, `fix`, `refactor`, `docs`, `chore`, `test`, `style`
- **Lowercase everything** -- no capital letters in the type or description
- **Short imperative description** (max ~72 chars) -- describe *what* the commit does, not *what you did*
- **Scope parentheses are optional** -- use where it adds clarity, e.g., `feat(tray): add auto-start toggle`, `fix(engine): prevent double-counting in pool mode`
- If a commit spans multiple concerns, use the most significant type (e.g., `feat` if it adds a feature even if it also updates docs)
- Multi-line body is optional but encouraged for non-trivial changes -- separate from subject with a blank line

### Important Patterns
- **Single-instance enforcement**: Named Mutex `NudgeAppTimeTracker` in `Program.cs`
- **Base directory resolution**: Dev mode walks up to find `Nudge.sln`; published mode uses exe directory. Controlled by `NUDGE_BASE_DIR` env var
- **State persistence**: Tracking state saved to `logs/state/tracking_state.json`
- **Timer-based polling**: `System.Threading.Timer` fires every `pollingIntervalMs` (default 1000ms)
- **FileSystemWatcher** with 500ms debounce for config hot-reload

## File Organization

- `config/config.json` -- Runtime configuration (tracked apps, schedules, global settings)
- `logs/` -- CSV usage logs (gitignored, generated at runtime)
- `logs/state/` -- Persistent tracking state (gitignored)
- `publish/` -- Build output for standalone exe
- `Nudge.sln` -- Solution file at repo root
- `src/Nudge/Nudge.csproj` -- Single project file

## Testing

There are currently no automated tests. When adding tests in the future:
- Create a test project at `tests/Nudge.Tests/Nudge.Tests.csproj`
- Use xUnit + FluentAssertions
- Mock Win32 APIs behind interfaces for testability
- Focus on `RuleEngine` (schedule resolution) and `TimeTracker` (time accumulation) as priority test targets

## Things to Watch Out For

- This is a **Windows-only** app. All code assumes Windows APIs are available.
- Modal warnings use Win32 `SetWindowPos(HWND_TOPMOST)` to appear over fullscreen games -- do not replace with standard WinForms `TopMost`.
- The day boundary is configurable (default 3 AM). Time calculations must use `dayBoundaryHour`, not midnight.
- Process names in config are **without `.exe`** (e.g., `"factorio"` not `"factorio.exe"`).
- `publish/` contains build artifacts and a copy of config -- changes to config structure may need the publish script updated too.

## Future Plans

The app is actively being expanded. Below are prioritized planned features with implementation notes.

### Priority Order

#### 1. Windows Auto-Start
**Complexity:** Easy | **Priority:** High | **Status:** Planned

Uses Windows registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Implementation:
- Add `autoStart` boolean to `GlobalSettings` (default `false`)
- On app start in `Program.cs` (after `configManager.Load()`), sync the registry key:
  - If `autoStart` is true, write registry value `"Nudge"` pointing to `Environment.ProcessPath`
  - If `autoStart` is false, remove the registry value if it exists
- Add a checkable `ToolStripMenuItem` ("Start with Windows") to `TrayIcon.CreateContextMenu()`
  - On click: toggle `config.GlobalSettings.AutoStart`, call `configManager.Save()`, and update the registry immediately
  - Read initial checked state from `config.GlobalSettings.AutoStart`
- **Dev mode guard**: Only register auto-start when running as a published exe (check that `Environment.ProcessPath` does NOT contain `dotnet.exe`). Show a tooltip or skip silently in dev mode.
- Use `Microsoft.Win32.Registry` (already available in `net8.0-windows`) -- no new dependencies

Files to modify: `GlobalSettings.cs`, `Program.cs`, `TrayIcon.cs`, `README.md`

#### 2. Weekend Grouping Config
**Complexity:** Easy | **Priority:** High | **Status:** Planned

Allow `"weekend"` as a schedule override key that applies to both Saturday and Sunday.

Implementation:
- No config model changes needed -- `AppSchedule.Overrides` is already `Dictionary<string, DaySchedule>`
- In `RuleEngine.ResolveSchedule()`, after the day-of-week override lookup fails (line ~37-40), add a fallback: if `dayName` is `"saturday"` or `"sunday"`, also try `schedule.Overrides.TryGetValue("weekend", ...)`
- Priority order: special date > individual day override > `"weekend"` override > default
- Backward compatible: existing `saturday`/`sunday` overrides still work and take precedence over `"weekend"`
- Update `README.md` with a `"weekend"` config example and document the priority order

Files to modify: `RuleEngine.cs`, `README.md`

#### 3. Multi-Source App Tracking (formerly "Shared Time Pools")
**Complexity:** Medium | **Priority:** High | **Status:** Planned

A single tracked app can monitor multiple process/browser sources, each with its own tracking mode. The app is "active" if **any** source is active. A single time counter accumulates for the whole app. This is the foundation that also enables browser tab tracking (Feature 5).

**Use case:** Track "Tibia" as one app that includes the game process, a wiki in Chrome, and YouTube videos about it -- all sharing one timer and one set of warnings/auto-close rules.

Config design -- add a `sources` array to `TrackedApp`:
```json
{
  "name": "Tibia",
  "sources": [
    { "processName": "tibia_game", "trackingMode": "process" },
    { "processName": "chrome", "trackingMode": "foreground" }
  ],
  "schedule": { ... }
}
```

Behavior:
- When `sources` is present and non-empty, it supersedes the legacy `processNames`/`trackingMode` fields
- When `sources` is absent/empty, the existing `processNames`/`trackingMode` behavior is preserved (backward compatible)
- The app is "active" if **any** source is currently active per its own tracking mode
- Time accumulates once per tick regardless of how many sources are active simultaneously (no double-counting)
- Warnings and auto-close use the app's single schedule against the single accumulated time counter
- Notifications display the app's `name` (e.g., "Tibia"), not individual source names

Implementation:
- Add `AppSource` model class in `Config/`:
  ```csharp
  public class AppSource
  {
      public string ProcessName { get; set; } = string.Empty;
      public string TrackingMode { get; set; } = "foreground";
  }
  ```
- Add `Sources` property to `TrackedApp`: `public List<AppSource>? Sources { get; set; }`
- Modify `AppMonitor` to add a method `IsAnySourceActive(TrackedApp app)` that iterates `app.Sources`, checks each source's process and tracking mode, and returns true if any is active
- Modify `NudgeEngine.ProcessApp()`:
  - If `app.Sources` is non-empty, call `IsAnySourceActive()` instead of the current `FindRunningProcess()` + `IsAppActive()` path
  - Everything else (time tracking, rule evaluation, warnings, auto-close) stays the same -- it already works on a per-app basis
- Process lifecycle tracking (`_trackedProcesses` dictionary) needs adjustment: track all source processes, detect session start when any source activates and session end when all sources deactivate

Files to modify: `TrackedApp.cs` (add Sources), new `Config/AppSource.cs`, `AppMonitor.cs`, `NudgeEngine.cs`, `README.md`

#### 4. Chrome Tab Content Tracking
**Complexity:** Medium-Hard | **Priority:** Medium | **Status:** Planned

Track time spent on specific browser tab content (e.g., pages with "Tibia" in title). Integrates with Feature 3's `sources` array as a new tracking mode.

**Approach: Chrome Extension + Localhost WebSocket**

1. A lightweight Nudge Chrome extension (installed once in Developer mode) monitors the active tab
2. Extension sends active tab URL/title to Nudge via localhost WebSocket
3. Nudge's `ChromeTabMonitor` matches against configured patterns and reports activity to `AppMonitor`

Config -- extends `AppSource` with a `tabPatterns` field and a `"browser-tab"` tracking mode:
```json
{
  "name": "Tibia",
  "sources": [
    { "processName": "tibia_game", "trackingMode": "process" },
    { "processName": "chrome", "trackingMode": "browser-tab", "tabPatterns": ["*Tibia*", "*tibia.com*"] }
  ],
  "schedule": { ... }
}
```

Implementation -- Nudge side:
- Add `TabPatterns` property to `AppSource`: `public List<string>? TabPatterns { get; set; }`
- Add `browserMonitorPort` to `GlobalSettings` (default `9123`)
- Add `Core/ChromeTabMonitor.cs`:
  - Listens on `localhost:{port}` for WebSocket connections using `System.Net.WebSockets` (built-in, no new dependencies)
  - Receives JSON messages: `{ "url": "...", "title": "...", "active": true/false }`
  - Exposes a method `IsTabMatchActive(List<string> patterns)` that checks if the current active tab matches any pattern
  - Uses glob/wildcard matching against tab title and URL
- `NudgeEngine` constructs and owns the `ChromeTabMonitor`; starts/stops it with the engine
- `AppMonitor.IsAnySourceActive()` (from Feature 3) handles the `"browser-tab"` case by querying `ChromeTabMonitor.IsTabMatchActive(source.TabPatterns)`
- Activity is determined entirely by WebSocket messages -- no process detection needed for browser-tab sources
- Handle WebSocket disconnection: treat as inactive (tab not matching). Extension will reconnect automatically.
- Multiple tabs matching patterns count as 1x active (not per-tab)

Implementation -- Chrome extension:
- Create `browser-extension/chrome/` folder at repo root (not inside `src/Nudge/`)
- `manifest.json` (Manifest V3) -- permissions: `tabs`, `activeTab`
- `background.js` (service worker) -- uses `chrome.tabs.onActivated` and `chrome.tabs.onUpdated` to detect active tab changes, connects to `ws://localhost:{port}`, sends `{ url, title, active }` messages
- No `content.js` needed -- the `chrome.tabs` API provides URL and title without injecting scripts into pages
- Optional `popup.html/js` for connection status display
- User installs extension once from the folder in Chrome Developer mode

Files to create: `Config/AppSource.cs` (if not created in Feature 3), `Core/ChromeTabMonitor.cs`, `browser-extension/chrome/manifest.json`, `browser-extension/chrome/background.js`
Files to modify: `AppSource.cs` (add TabPatterns), `GlobalSettings.cs` (add BrowserMonitorPort), `AppMonitor.cs`, `NudgeEngine.cs`, `README.md`

**Notes:**
- Feature 3 (multi-source tracking) must be implemented first as this builds on it
- Consider Firefox support later (WebExtensions API is similar)

#### 5. Schedule Groups (Config Convenience)
**Complexity:** Easy-Medium | **Priority:** Low | **Status:** Planned

Define a named schedule once and reference it from multiple tracked apps. This is purely a config DRY convenience -- it doesn't change runtime behavior. Each app still tracks time independently.

Config design:
```json
{
  "scheduleGroups": {
    "gaming": {
      "default": { "warningMilestones": [...], "autoClose": { ... } },
      "overrides": { "weekend": { ... } }
    }
  },
  "trackedApps": [
    { "name": "Game A", "processNames": ["game_a"], "scheduleGroup": "gaming" },
    { "name": "Game B", "processNames": ["game_b"], "scheduleGroup": "gaming" }
  ]
}
```

Implementation:
- Add `ScheduleGroups` to `NudgeConfig`: `public Dictionary<string, AppSchedule>? ScheduleGroups { get; set; }`
- Add `ScheduleGroup` to `TrackedApp`: `public string? ScheduleGroup { get; set; }`
- In `RuleEngine.ResolveSchedule()`, if `app.ScheduleGroup` is set, look up the schedule from `config.ScheduleGroups` instead of `app.Schedule`
- If `app.ScheduleGroup` is set but `app.Schedule` also has values, the group takes precedence (or error/warn)
- `RuleEngine.ResolveSchedule()` needs access to the config's schedule groups -- pass them as a parameter or restructure slightly

Files to modify: `NudgeConfig.cs`, `TrackedApp.cs`, `RuleEngine.cs`, `NudgeEngine.cs`, `README.md`

---

When implementing new features, consider how they fit into the existing namespace structure and event-driven architecture.
