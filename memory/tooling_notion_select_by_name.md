---
name: tooling-notion-select-by-name
description: ALTER COLUMN SET SELECT matches options BY NAME — renaming or omitting one deletes it and empties every page that used it; adding one is safe. Dump values first; new options are hidden on filtered views.
metadata:
  type: reference
---

`notion-update-data-source` with `ALTER COLUMN "Prop" SET SELECT('name':color, ...)` replaces the whole option list. Notion matches options **by name**:
- Name unchanged → option and all page values survive. **Adding** an option is safe if every existing name is passed unchanged.
- Name changed or omitted → the option is deleted and every page using it silently ends up empty. The call still reports success.

**Related facts:**
- `notion-create-pages` rejects an unknown select value — the option must exist first.
- Deleting an option does not delete the stored value; recreating the same name restores it.
- A new option is **invisible on views that filter that property** until it is added to the filter. Check the views after adding one. In `notion-update-view`, `CLEAR FILTER` leaves filter-bar entries in place — remove them with `CLEAR QUICK FILTER "<Prop>"` and read the view back.

**How to apply:**
- Before any `SET SELECT`, dump current values: `SELECT "userDefined:ID", "<Prop>", url FROM "collection://…" WHERE "<Prop>" IS NOT NULL`.
- Pass every existing option with its colour. Re-query afterwards and compare counts.
- To rename a heavily used option, use the Notion UI (keeps the option ID).

Related: [[tooling-notion-update-content-drops-properties]].
