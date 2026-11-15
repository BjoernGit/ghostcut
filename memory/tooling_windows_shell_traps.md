---
name: tooling-windows-shell-traps
description: "Windows shell traps: PowerShell reads „ “ ” as string delimiters; backslashes in Bash-tool heredocs get eaten; shell scripts must be checked out with LF."
metadata:
  type: reference
---

- **PowerShell (5.1 and 7) treats typographic quotes `„ “ ” ‘ ’` as quote characters.** `"„$name“ läuft"` ends the string at `„` and the file no longer parses. Use `'…'` or plain `"` in PS strings.
- **Heredocs in the Bash tool:** `\\` inside a quoted heredoc (`<<'EOF'`) has arrived as `\`. Write scripts with the Write tool, then run the file.
- **`.sh` files need LF.** git on this machine converts to CRLF on checkout; bash then fails with `$'\r': command not found`. `.gitattributes` pins `*.sh` to LF — keep it that way. Check line endings byte-wise (`tr -cd '\r' < file | wc -c`), not with `grep $'\r'`.

Related: [[feedback-working-with-bjoern]].
