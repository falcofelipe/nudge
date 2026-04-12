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

### Priority Order (by complexity/impact)

#### 1. Windows Auto-Start
**Complexity:** Easy | **Priority:** High

Uses Windows registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Implementation:
- Add `autoStart` boolean to `GlobalSettings`
- On app start, check if auto-start is enabled and register/unregister the registry key
- Add tray menu option to toggle auto-start
- Requires registry write permissions (typically available)

#### 2. Weekend Grouping Config
**Complexity:** Easy | **Priority:** High

Merge `saturday` and `sunday` under a single `weekend` key.

Implementation:
- Add `"weekend"` as a special override key alongside individual day names
- In `RuleEngine.ResolveSchedule()`, expand `"weekend"` to match both Saturday and Sunday
- Backward compatible: existing `saturday`/`sunday` overrides still work
- Update README.md config examples

#### 3. Shared Time Pools
**Complexity:** Medium | **Priority:** High

Multiple apps share accumulated time (e.g., "Tibia game" + "Tibia wiki" count together).

Implementation:
- Add `timePoolId` (string?) to `TrackedApp`
- Add `TimePoolTracker` class that groups `TimeTracker` states by pool ID
- In `NudgeEngine.ProcessApp()`, use pool's combined active state instead of individual app state
- Apps without a `timePoolId` track independently (existing behavior)
- When any app in a pool is active, all apps in that pool count time

#### 4. Multiple Apps, Same Schedule, Separate Counters
**Complexity:** Medium | **Priority:** Medium

Track multiple apps with the same warning/auto-close schedule but independent time counters.

Implementation:
- Add `scheduleGroup` (string?) to `TrackedApp`
- Add `ScheduleGroup` class that holds shared milestone/auto-close config
- `RuleEngine` resolves schedule from group, but time tracking remains per-app
- Useful for tracking related apps (e.g., Steam + a game) with same limits

#### 5. Chrome Tab Content Limits
**Complexity:** Medium-Hard | **Priority:** Medium

Track time spent on specific browser tab content (e.g., pages with "Tibia" in title).

**Approach: Chrome Extension + Localhost WebSocket**

Chrome extensions cannot be injected into running instances. Instead:
1. A lightweight Nudge Chrome extension is installed once
2. Extension detects active tab URL/title and matches against patterns
3. Extension sends data to Nudge via localhost WebSocket (port 9123)
4. Nudge's `AppMonitor` handles WebSocket messages and updates time tracking

Implementation:
- Create `BrowserTracking/` folder with extension files
- Add `ChromeTabMonitor.cs` in `Core/` that:
  - Listens on localhost:9123 for WebSocket connections
  - Parses messages: `{ "url": "...", "title": "..." }`
  - Matches against configured `browserTabPatterns`
- Add `browserTabPatterns` array to `TrackedApp`:
  ```json
  {
    "name": "Tibia Browsing",
    "processNames": ["chrome"],
    "browserTabPatterns": ["*Tibia*", "*tibia.com*"],
    "trackingMode": "browser-tab"
  }
  ```
- Add `trackingMode: "browser-tab"` (new mode alongside "process" and "foreground")
- Multiple tabs matching patterns count as 1x (not per-tab)

**Extension Files Needed:**
- `manifest.json` (Manifest V3)
- `background.js` (service worker)
- `content.js` (injected script)
- `popup.html/js` (optional settings UI)

**Notes:**
- User installs extension once from `BrowserTracking/` folder (Developer mode)
- Extension runs in all Chrome tabs
- No debug port needed -- cleaner than CDP approach
- Consider Firefox support later (WebExtensions API is similar)

---

When implementing new features, consider how they fit into the existing namespace structure and event-driven architecture.
