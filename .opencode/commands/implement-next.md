---
description: Implement the next planned feature from the priority list
---

Implement the next feature from the "Future Plans" section in `AGENTS.md` that has **Status: Planned**.

Before writing any code:
1. Read `AGENTS.md` to find the next planned feature (lowest number with Status: Planned)
2. Read `README.md` to understand the full app structure and current config format
3. Read all source files listed in the feature's "Files to modify" / "Files to create" sections
4. Create a detailed todo list breaking down the implementation into small steps
5. If the feature plan references other features as dependencies, verify those are already implemented (Status: Done)

When implementing:
- Follow the detailed implementation notes in the feature plan exactly
- Follow existing code style: file-scoped namespaces, one class per file, PascalCase public / _camelCase private
- Use events for component communication -- avoid tight coupling
- If the feature needs new config options, follow the config model pattern (add to model class, use nullable types with sensible defaults, update README)
- If it plugs into the monitoring loop, wire it through `NudgeEngine`
- Use `System.Text.Json` (never Newtonsoft) with camelCase naming for any new JSON
- Add Win32 P/Invoke declarations with clear comments explaining the function
- Remember this is Windows-only; Win32 APIs are always available

After implementing:
- Run `dotnet build` and fix any errors (use the full SDK path: `& "C:\Users\falcof\AppData\Local\dotnet-sdk\dotnet.exe" build`)
- Update `README.md` if the feature adds user-facing functionality or new config options
- Update `AGENTS.md`: change the implemented feature's Status from `Planned` to `Done`
- Update `AGENTS.md` if the feature changes the architecture or adds new files/namespaces
- Summarize what was implemented and what files were changed
