---
name: feedback-retry-blocked-tool-call
description: A tool call refused by the permission classifier is not a permanent capability limit — retry it once before telling Björn it cannot be done.
metadata:
  type: feedback
---

"Permission for this action was denied by the auto mode classifier" is **not deterministic**. Retry the identical call once before concluding the capability is unavailable.

**Why:** In the Lua project an agent reported a Notion schema change as impossible; the identical call went through on the next attempt.

**How to apply:**
- Retry once. After a second refusal, say plainly *what* was refused and offer the manual route — as a session permission issue, not "this cannot be done".
- Distinguish the layers: an API rejecting the payload (e.g. Notion refusing an unknown select value) is a real error; only the local classifier refusal is worth retrying.
- Do not retry blindly when the action is destructive.

Related: [[tooling-notion-select-by-name]].
