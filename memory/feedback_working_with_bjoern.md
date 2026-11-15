---
name: feedback-working-with-bjoern
description: "How Björn wants to work: a question is not a go, decide details yourself and name the assumption, one plain-text question, TL;DR after long work, what counts as a merge go."
metadata:
  type: feedback
---

Carried over from the Lua Coding Game project, where these rules grew from real incidents.

**A question is not a go.** "Wäre es möglich…?", "kann man…?", "geht das…?" ask for a discussion: feasibility, cleanest approach, main trade-off, then stop. Verification questions ("hat das gerade keinen Effekt, oder?") get an answer, not a change. Imperatives ("mach", "leg los", "setz es um") are a go.

**Once there is a go, decide yourself — Ghostcut runs in autonomous mode** (Björn, 2026-11-15: "mit Kleinigkeiten brauchst du mich gar nicht behelligen … mach einfach mal und schreib mir später deine Entscheidungen", like the unattended agents in his other project). He wants the fastest route to a working result and corrects afterwards.
- Decide and build, also for mid-sized design choices (defaults, UI details, file layout, library picks, behaviour Björn did not specify). Name each decision in one line while working and again under `Entscheidungen:` / `Annahmen:` in the report. An unnamed assumption counts as concealed.
- Ask **before** implementing only when a choice could be seriously different from what Björn probably expects — it changes how the ghost timeline behaves, or it is costly to undo.
- Concerns are not a stop sign: take the better option, build it, flag it.

**Why:** every needless question stalls the work; Björn switches between several agents.

**Questions as plain text**, one load-bearing question, recommendation + trade-off in two lines. No multiple-choice forms.

**Narration:** one sentence on what you are about to do, a short update on direction changes. The final report states the outcome (what works, what is open, assumptions), not the path.

**End every longer job with a TL;DR**: a short bullet block at the very end that stands on its own — Björn often comes back only when the job is done.

**Merge go (to `main`):** "merge bitte", "los", "ab auf main", or a direct "ja" to a merge you just proposed. Not a go: "funktioniert", "sieht gut aus", praise. Every merge is a fresh gate. A merge order is also the Done sign-off for the ticket.

**A setting is a setting:** when Björn asks for a value, change that value — don't build machinery around it.

Related: [[feedback-retry-blocked-tool-call]].
