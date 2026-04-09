---
description: Commit and push changes to git
---

Commit and push the current changes:
1. Run `git status` and `git diff` to review what has changed
2. Stage all relevant changes (do NOT stage files in `logs/`, `bin/`, `obj/`, `publish/`, or files that may contain secrets)
3. Write a concise commit message that describes the "why", following conventional style (e.g. "add pomodoro timer feature", "fix day boundary calculation", "update config model for focus mode")
4. Commit the changes
5. Push to the current remote branch

If there are no changes to commit, say so and stop. If the branch has no upstream, push with `-u origin <branch>`.
