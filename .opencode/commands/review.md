---
description: Review recent changes for issues
---

Review the recent changes in this project:
1. Run `git diff` to see unstaged changes, and `git diff --cached` for staged changes
2. Check for:
   - Build errors (`dotnet build`)
   - Code style violations (file-scoped namespaces, naming conventions, one class per file)
   - Tight coupling that should use events instead
   - Missing null checks (nullable reference types are enabled)
   - Any use of Newtonsoft.Json (should be System.Text.Json)
   - Config changes that are missing README updates
   - Win32 P/Invoke declarations missing comments
   - Hardcoded midnight boundaries (should use dayBoundaryHour)
   - Process names with .exe extension (should be without)
3. Summarize findings and suggest fixes
