---
description: Add a new config option to the app
---

Add a new configuration option: $ARGUMENTS

Follow the project's config model pattern:
1. Add the property to the appropriate model class in `src/Nudge/Config/` using nullable types with sensible defaults for backward compatibility
2. Wire it up in the relevant component (engine, notifications, etc.)
3. Update `config/config.json` with an example value
4. Update the configuration tables in `README.md`
5. Run `dotnet build` to verify no errors
6. Verify the hot-reload path in `ConfigManager` will pick up the new field automatically
