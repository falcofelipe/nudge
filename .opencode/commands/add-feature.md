---
description: Plan and implement a new feature
---

Implement the following feature: $ARGUMENTS

Before writing any code:
1. Read `README.md` to understand the full app structure
2. Identify which namespace(s) this feature belongs in (Core, Config, Notifications, UI, Logging, or a new one)
3. Plan which files need to be created or modified

When implementing:
- Follow existing code style: file-scoped namespaces, one class per file, PascalCase public / _camelCase private
- Use events for component communication -- avoid tight coupling
- If the feature needs new config options, follow the config model pattern (add to model class, use nullable types, update README)
- If it plugs into the monitoring loop, wire it through `NudgeEngine`
- Use `System.Text.Json` (never Newtonsoft) with camelCase naming for any new JSON
- Add Win32 P/Invoke declarations with clear comments explaining the function
- Remember this is Windows-only; Win32 APIs are always available

After implementing:
- Run `dotnet build` and fix any errors
- Update `README.md` if the feature adds user-facing functionality or new config options
- Update `AGENTS.md` if the feature changes the architecture
