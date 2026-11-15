---
name: tooling-notion-gc-number-lookup
description: Resolve "GC-N" via notion-query-data-sources SQL on userDefined:ID — never via notion-search with a bare number. Quota exhausted → view mode.
metadata:
  type: reference
---

The Ghostcut TODOs database has an auto-increment ID (`userDefined:ID`, shown as `GC-N`). Semantic search does **not** index it — a bare number only matches text in other pages.

**How to apply:**
- Björn names a ticket by number → one SQL call, then `notion-fetch` the url:
  `SELECT "userDefined:ID", Task, Status, url FROM "collection://419b7847-b9dd-81d1-a552-000b0ec34c8f" WHERE "userDefined:ID" = ?` with param `[N]`.
- The same tool filters any property, e.g. `WHERE Status NOT IN ('Done', 'Won''t Do')`.
- Batch lookups into one query (`WHERE "userDefined:ID" IN (...)`) — SQL queries are rate-limited per **workspace**, and the Lua project shares that quota.
- **Quota exhausted (`usage_limit_reached`):** rows mode counts against it too; only **view mode** is free. Fall back to view mode on a database view (fetch the database to list its views) or `notion-search` with title keywords plus `data_source_url`. Never retry blocked SQL in a loop.

Related: [[tooling-notion-select-by-name]].
