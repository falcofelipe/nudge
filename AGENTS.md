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
├── Core/                   # Engine, monitoring, time tracking, rules, app killing, auto-start, browser tab monitor
├── Config/                 # JSON config models + hot-reload ConfigManager (incl. AppSource for multi-source/browser-tab tracking)
├── Notifications/          # Toast (UWP toolkit) + Modal (Win32 topmost dialog)
├── UI/                     # System tray icon + context menu
└── Logging/                # CSV event logging

browser-extension/chrome/   # Chrome extension for browser tab tracking (WebSocket client)
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
# IMPORTANT: Nudge.exe locks the file while running. Always kill it before publishing.
Stop-Process -Name Nudge -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1
$env:DOTNET_ROOT = "C:\Users\falcof\AppData\Local\dotnet-sdk"
& $dotnet publish src/Nudge -c Release -o ./publish
# Then copy config: Copy-Item -Force .\config\config.example.json .\publish\config\

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

### WinForms UI Layout Rules
When creating or modifying WinForms UI (forms, dialogs, panels):
1. **Avoid deeply nested layout containers.** `TableLayoutPanel` and `FlowLayoutPanel` have compounding margin/padding behavior that causes clipping. Prefer flat `Panel > Controls` with `Anchor` for right/bottom-aligned elements.
2. **`Height` vs `ClientSize.Height`**: `Form.Height` includes the title bar (~39px). Always calculate *client area* needed and convert: `Height = desiredClientHeight + (Height - ClientSize.Height)`.
3. **`Margin` is ignored on docked controls** inside plain `Panel` containers. Margins only take effect inside `TableLayoutPanel` or `FlowLayoutPanel`. If you need spacing between docked controls, bake it into the control's `Height` directly.
4. **Use named constants for all spacing values** (`ContentTop`, `RowHeight`, `FooterHeight`, etc.) and reference them in *both* the control positioning *and* the form sizing formula. Never use separate literal values that must be kept in sync manually.
5. **`AutoScroll = true` reserves scrollbar gutter space** even when content fits. Only enable it when overflow is genuinely expected.
6. **Use `Anchor` for responsive positioning** (e.g., `AnchorStyles.Top | AnchorStyles.Right` for right-aligned labels) instead of hardcoded pixel coordinates captured at construction time.
7. **When changing any spacing constant, trace the full sizing chain**: control positions, panel sizes, form height calculation, and verify they all use the same constants.

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

- `config/config.example.json` -- Example/template configuration (tracked in git)
- `config/config.json` -- Runtime configuration (gitignored, created from example on first run)
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
**Complexity:** Easy | **Priority:** High | **Status:** Done

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
**Complexity:** Easy | **Priority:** High | **Status:** Done

Allow `"weekend"` as a schedule override key that applies to both Saturday and Sunday.

Implementation:
- No config model changes needed -- `AppSchedule.Overrides` is already `Dictionary<string, DaySchedule>`
- In `RuleEngine.ResolveSchedule()`, after the day-of-week override lookup fails (line ~37-40), add a fallback: if `dayName` is `"saturday"` or `"sunday"`, also try `schedule.Overrides.TryGetValue("weekend", ...)`
- Priority order: special date > individual day override > `"weekend"` override > default
- Backward compatible: existing `saturday`/`sunday` overrides still work and take precedence over `"weekend"`
- Update `README.md` with a `"weekend"` config example and document the priority order

Files to modify: `RuleEngine.cs`, `README.md`

#### 3. Multi-Source App Tracking (formerly "Shared Time Pools")
**Complexity:** Medium | **Priority:** High | **Status:** Done

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
**Complexity:** Medium-Hard | **Priority:** Medium | **Status:** Done

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

#### 5. Post-Limit Recurring Warnings
**Complexity:** Easy | **Priority:** High | **Status:** Done

When auto-close is disabled and all warning milestones have fired, re-fire the last milestone's warning as a modal every N minutes until the user closes the app. Currently, once the last milestone fires, nothing else happens -- the user can keep using the app indefinitely with no further nudges.

Config -- add `postLimitRepeatIntervalMinutes` to `DaySchedule`:
```json
{
  "schedule": {
    "default": {
      "warningMilestones": [
        { "afterMinutes": 30, "type": "toast", "message": "30 minutes played" },
        { "afterMinutes": 60, "type": "modal", "message": "1 hour - time to stop!" }
      ],
      "postLimitRepeatIntervalMinutes": 5
    }
  }
}
```

Behavior:
- Only activates when ALL of: (a) all milestones have been fired, (b) auto-close is disabled or not configured, (c) `postLimitRepeatIntervalMinutes` is set and > 0
- Re-fires the **last milestone's warning** (by `AfterMinutes` order) as a **modal** every `postLimitRepeatIntervalMinutes` minutes, regardless of the original milestone's `type`
- The message is the last milestone's message -- no separate message config needed
- If auto-close IS enabled, this setting is ignored (the app will be killed, so recurring warnings are pointless)

Implementation:
- Add `PostLimitRepeatIntervalMinutes` property to `DaySchedule`: `public int? PostLimitRepeatIntervalMinutes { get; set; }`
- Add `LastPostLimitWarningMinutes` to `AppTimeState` (in `TimeTracker.cs`): `public double? LastPostLimitWarningMinutes { get; set; }` -- tracks accumulated time when the last nag was shown
- Reset `LastPostLimitWarningMinutes` on day boundary reset (same as other state fields)
- In `RuleEngine`, add a new method:
  ```csharp
  public bool ShouldFirePostLimitWarning(
      DaySchedule schedule, double accumulatedMinutes,
      HashSet<int> firedMilestoneMinutes, double? lastPostLimitWarningMinutes)
  ```
  Returns true when: all milestones are fired, auto-close is disabled/null, `postLimitRepeatIntervalMinutes > 0`, and `accumulatedMinutes >= (lastPostLimitWarningMinutes ?? lastMilestoneMinutes) + interval`
- In `NudgeEngine.ProcessApp()`, after the milestone check block, add a post-limit check:
  - Call `RuleEngine.ShouldFirePostLimitWarning()`
  - If true, fire a modal warning using the last milestone's message, update `LastPostLimitWarningMinutes` to current `accumulatedMinutes`
- `MergeWithDefault` in `RuleEngine` should also merge `PostLimitRepeatIntervalMinutes` (override wins if set, otherwise inherit default)

Files to modify: `DaySchedule.cs`, `TimeTracker.cs` (AppTimeState), `RuleEngine.cs`, `NudgeEngine.cs`, `README.md`

#### 6. Weekly Bonus Time
**Complexity:** Medium | **Priority:** Medium | **Status:** Planned

A configurable weekly pool of extra minutes that the user can "spend" to extend their time limit on any app. Designed as a controlled pressure-release valve -- rather than just closing the app and reopening it, the user gets a limited, conscious choice to continue.

**Use case:** You've used 60 minutes of Factorio (your daily limit), the final modal warning fires. Instead of dismissing it and feeling guilty, you click "Use 15min bonus" and get a legitimate 15-minute extension. You have 30 minutes of bonus per week across all apps, so you use it deliberately.

Config -- add `bonusTime` to `globalSettings`:
```json
{
  "globalSettings": {
    "bonusTime": {
      "weeklyMinutes": 30,
      "incrementMinutes": 15
    }
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `weeklyMinutes` | int | `30` | Total bonus minutes available per week, shared across all apps |
| `incrementMinutes` | int | `15` | How many minutes are spent per use (each button click) |

Behavior:
- The weekly bonus pool is **shared across all tracked apps** -- using 15 minutes on Factorio leaves 15 for everything else
- Bonus time is offered via the **modal warning UI** -- a "Use Xmin bonus (Y remaining)" button appears alongside "I understand"
- Spending bonus time **adds an offset** to the app's time state, effectively pushing future milestones and auto-close forward by `incrementMinutes`
- Already-fired milestones are NOT re-evaluated or un-fired -- bonus time only delays future thresholds
- If the remaining weekly pool is less than `incrementMinutes`, the button is disabled/hidden
- The weekly pool resets every Monday at the configured `dayBoundaryHour` (or configurable reset day)
- Bonus time persists across app restarts via state file
- Bonus can be used multiple times on the same modal (or successive modals) until the weekly pool is depleted
- Also integrates with the post-limit recurring warnings (Feature 5) -- each nag modal also offers the bonus button

State tracking:
- Add `BonusTimeState` to state persistence (in `tracking_state.json` or a separate `bonus_state.json`):
  ```csharp
  public class BonusTimeState
  {
      public double UsedMinutes { get; set; }
      public string WeekStartDate { get; set; } = string.Empty; // yyyy-MM-dd of the Monday
  }
  ```
- On each engine tick (or on bonus use), check if `WeekStartDate` is still the current week. If not, reset `UsedMinutes` to 0.
- Per-app bonus offset: add `BonusMinutesApplied` to `AppTimeState` -- tracks how many bonus minutes have been applied to this app today. Resets with the daily state.

Implementation:
- Add `Config/BonusTimeConfig.cs`:
  ```csharp
  public class BonusTimeConfig
  {
      public int WeeklyMinutes { get; set; } = 30;
      public int IncrementMinutes { get; set; } = 15;
  }
  ```
- Add `BonusTime` property to `GlobalSettings`: `public BonusTimeConfig? BonusTime { get; set; }`
- Add `BonusMinutesApplied` to `AppTimeState` (default 0, resets daily)
- Add `BonusTimeState` tracking to `TimeTracker` (weekly state, persisted)
- Modify `RuleEngine` methods to factor in bonus offset:
  - `GetPendingWarnings`: compare `accumulatedMinutes` against `milestone.AfterMinutes + bonusMinutesApplied`
  - `ShouldAutoClose`: compare against `autoClose.AfterMinutes + bonusMinutesApplied`
  - `ShouldSendPreCloseWarning`: same offset adjustment
  - `ShouldFirePostLimitWarning`: same offset adjustment
- Modify `ModalWarning.ShowWarning()` to accept bonus time parameters and show a "Use Xmin bonus" button:
  - Add parameters: `bonusAvailable` (bool), `bonusIncrementMinutes` (int), `bonusRemainingMinutes` (double)
  - Add a second button: "Use {increment}min bonus ({remaining}min left this week)"
  - Return a result indicating whether the user clicked "I understand" or "Use bonus"
  - Since the modal runs on a background thread, use a callback or `Task<ModalResult>` pattern to communicate the choice back to the engine
- Modify `NudgeEngine.ProcessApp()`:
  - When firing a modal warning, pass bonus availability info
  - If the user chooses bonus: call `TimeTracker.ApplyBonus()` which increments `BonusMinutesApplied` for the app and `UsedMinutes` in the global `BonusTimeState`
- Modify tray icon status to show remaining weekly bonus

Files to create: `Config/BonusTimeConfig.cs`
Files to modify: `GlobalSettings.cs`, `TimeTracker.cs`, `RuleEngine.cs`, `NudgeEngine.cs`, `ModalWarning.cs`, `TrayIcon.cs`, `README.md`

**Dependencies:** Should be implemented after Feature 5 (post-limit warnings) so the bonus button can appear on nag modals too.

#### 7. Settings UI
**Complexity:** Hard | **Priority:** Low | **Status:** Planned

A WinForms settings window accessible from the tray menu that replaces manual JSON editing. Should look and feel like classic Windows settings dialogs -- functional, no-frills forms with tabs and standard controls.

**This feature replaces the previously planned "Schedule Groups" feature.** The original idea of defining named schedules in JSON and referencing them by key is unnecessary when a UI provides a "Copy schedule from..." button that duplicates values inline. This is simpler for users and avoids indirection in the config.

**Prerequisite:** Implement after Features 3-6 are done so the config shape is stable. Building the UI before multi-source tracking, browser tabs, and bonus time are implemented means rework every time a new config field is added.

UI structure -- tabbed WinForms dialog:
```
[Global Settings] [Tracked Apps] [About]
```

**Global Settings tab:**
- Polling interval (numeric input)
- Default tracking mode (dropdown: process/foreground)
- Day boundary hour (numeric input 0-23)
- Log usage data (checkbox)
- Exit confirmation (checkbox)
- Auto-start with Windows (checkbox)
- Bonus time settings: weekly minutes, increment minutes (numeric inputs)
- Browser monitor port (numeric input, from Feature 4)

**Tracked Apps tab:**
- Left panel: list of tracked apps with add/remove buttons
- Right panel: edit form for the selected app
  - Name (text input)
  - Process names (editable list)
  - Sources (editable list with process name + tracking mode + optional tab patterns, from Feature 3/4)
  - Tracking mode (dropdown)
  - Enabled (checkbox)
  - Schedule section:
    - Default day schedule: warning milestones (data grid) + auto-close settings + post-limit repeat interval
    - Overrides: add/edit day-of-week or "weekend" overrides
    - Special dates: add/edit date overrides
  - **"Copy schedule from..."** button: dropdown listing other tracked apps, copies the selected app's entire schedule (default + overrides + special dates) into the current app's config. Replaces the Schedule Groups concept with a simpler one-time action.

**About tab:**
- App version, links, basic info

Implementation:
- Create `UI/SettingsForm.cs` -- main tabbed form, 800x600 or resizable
- Create `UI/GlobalSettingsPanel.cs` -- user control for global settings tab
- Create `UI/TrackedAppsPanel.cs` -- user control for tracked apps tab, master-detail layout
- Create `UI/ScheduleEditor.cs` -- reusable user control for editing a DaySchedule (milestones grid + auto-close fields)
- Wire "Settings..." menu item in `TrayIcon.CreateContextMenu()` to open the form
- On save: serialize the form state back to the config models, call `ConfigManager.Save()`
- On open: populate from current `ConfigManager.Config`
- Handle concurrent edits: if `FileSystemWatcher` fires while the settings form is open, prompt the user to reload or keep their changes
- Use standard WinForms controls: `TabControl`, `DataGridView` for milestones, `NumericUpDown` for numbers, `ComboBox` for dropdowns, `CheckBox` for booleans, `ListBox` for app list
- No third-party UI libraries -- keep it stock WinForms

Files to create: `UI/SettingsForm.cs`, `UI/GlobalSettingsPanel.cs`, `UI/TrackedAppsPanel.cs`, `UI/ScheduleEditor.cs`
Files to modify: `TrayIcon.cs`, `README.md`

**Notes:**
- This supersedes Feature 5 (Schedule Groups). The "Copy schedule from..." button achieves the same DRY goal with less config complexity.
- The form should work with the current JSON config format -- no config model changes needed for the UI itself (it reads/writes the existing models).
- Consider keyboard shortcuts and tab order for accessibility.

---

When implementing new features, consider how they fit into the existing namespace structure and event-driven architecture.
