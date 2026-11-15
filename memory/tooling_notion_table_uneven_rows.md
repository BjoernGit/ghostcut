---
name: tooling-notion-table-uneven-rows
description: "A Notion table whose rows carry more cells than the header gets normalised on the next edit — the extra cells are DROPPED. Fetch, count cells per row, rebuild before inserting."
metadata:
  type: reference
---

Inserting one row into a table whose rows had more cells than its header made Notion normalise the table to the header width and silently drop the extra cells of every row. The edit tool reported success.

**How to apply:**
- Before inserting into a Notion `<table>`, fetch it and check every `<tr>` has as many `<td>` as the header. If not, rebuild the whole table in one replace, never append into it.
- After any table edit, fetch again and compare the row count.
- Keep the pre-edit fetch in the session — it may be the only copy of lost rows.

Related: [[tooling-notion-markdown-format]].
