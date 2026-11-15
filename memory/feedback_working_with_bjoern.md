---
name: feedback-working-with-bjoern
description: "How Björn wants to work: a question is not a go, decide details yourself and name the assumption, one plain-text question, TL;DR after long work, what counts as a merge go."
metadata:
  type: feedback
---

Carried over from the Lua Coding Game project, where these rules grew from real incidents.

**A question is not a go.** "Wäre es möglich…?", "kann man…?", "geht das…?" ask for a discussion: feasibility, cleanest approach, main trade-off, then stop. Verification questions ("hat das gerade keinen Effekt, oder?") get an answer, not a change. Imperatives ("mach", "leg los", "setz es um") are a go.

**Once there is a go, decide details yourself.** Test: *does the choice shape a substantial part of the feature, or is it expensive to undo?*
- No (naming, placement, defaults, which of two equivalent APIs, test layout, small refactors) → decide, name the assumption in one line while working and again under `Annahmen:` in the report. An unnamed assumption counts as concealed.
- Yes (architecture direction, project file format, rework across many files, the core of a taste feature such as how the ghost timeline feels) → ask **before** implementing.
- Unsure → it usually isn't load-bearing.

**Why:** Björn switches between several agents; every needless question costs a context switch.

**Questions as plain text**, one load-bearing question, recommendation + trade-off in two lines. No multiple-choice forms.

**Narration:** one sentence on what you are about to do, a short update on direction changes. The final report states the outcome (what works, what is open, assumptions), not the path.

**End every longer job with a TL;DR**: a short bullet block at the very end that stands on its own — Björn often comes back only when the job is done.

**Merge go (to `main`):** "merge bitte", "los", "ab auf main", or a direct "ja" to a merge you just proposed. Not a go: "funktioniert", "sieht gut aus", praise. Every merge is a fresh gate. A merge order is also the Done sign-off for the ticket.

**A setting is a setting:** when Björn asks for a value, change that value — don't build machinery around it.

Related: [[feedback-retry-blocked-tool-call]].
