---
name: tooling-notion-markdown-format
description: "Notion MCP page content is Notion-flavoured Markdown — tables as <table> XML, tabs for nesting, escape special characters outside code; long pages via create + insert_content chunks."
metadata:
  type: reference
---

`notion-create-pages` / `notion-update-page` content is **Notion-flavoured Markdown**, not GFM (spec: fetch `notion://docs/notion-flavored-markdown-spec`):
- **Tables** as `<table header-row="true"><tr><td>…</td></tr></table>` — convert GFM pipe tables. Cells hold rich text only.
- **Nesting uses tabs**, not spaces.
- **Escape** `\ * ~ ` $ [ ] < > { } | ^` in plain text (not inside code, not in link URLs). A bare `~4 h` needs `\~`.
- The title goes into the `title` property, never as `# H1` in the content.
- Internal Notion links are auto-converted to page mentions.

**Long pages:** create with the first chunk, then append with `insert_content` + `position: end`, one section per call.

Related: [[tooling-notion-table-uneven-rows]], [[tooling-notion-update-content-drops-properties]].
