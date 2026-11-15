---
name: tooling-notion-update-content-drops-properties
description: notion-update-page with update_content or insert_content silently ignores a `properties` field; property changes need a separate update_properties call.
metadata:
  type: reference
---

`notion-update-page` runs one `command`. Sending `properties` together with `update_content` (or `insert_content`) returns success, but only the content changes.

**How to apply:** change content and properties in two calls (`update_content`/`insert_content`, then `update_properties`), and read the property back via SQL or fetch.

Related: [[tooling-notion-select-by-name]].
