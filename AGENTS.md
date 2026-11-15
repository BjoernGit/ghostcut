# Ghostcut — Agent Project Context

This file is the **single source** of project conventions and applies to every agent,
regardless of tool (Claude Code, Codex, …). `CLAUDE.md` only imports it. It is **complete** —
an agent needs no knowledge from other projects. It holds only what cannot be read from the
code; recipes and pitfalls live in the memory (`memory/MEMORY.md`).

## Project
A video editor of our own for trailers and devlogs of Björn's game (trigger: the Steam trailer).
The core idea is the **ghost timeline**: clips alternate between two lanes on one alignment
line, each new clip starts with its leading edge exactly at the cut edge of the previous one, and
trimmed-off parts stay visible as greyed-out overhangs. On top: a voice track, Whisper
transcription (local) rendered as real text in the video, voice processing (compressor, EQ, …)
and export in common qualities.

## Tech stack (GC-1)
- **C#/.NET 10 + Avalonia**, Windows first, nothing Windows-exclusive without need.
- The timeline is a custom, freely drawn control — not a stock widget.
- Preview: decode frames via ffmpeg bindings and render them ourselves; proxies as fallback.
- Export via ffmpeg. Speech-to-text via Whisper.net (whisper.cpp), local. Audio via NAudio.
- Rationale and assumptions are in GC-1.

## Session start (mandatory)
1. Set the chat title `GC-<topic>` as soon as the topic is clear from Björn's first message
   (Claude Desktop: `set_session_title`; skip if the tool cannot set titles).
2. The SessionStart hook (`tools/agent/session-start.sh`) fast-forwards `main` via
   `git pull --ff-only` when the working tree is clean, and reports branch and pending changes.
   Other agents run it by hand.
3. Read `memory/MEMORY.md` (Claude Code loads it automatically once set up — see Memory).
4. Before ticket work, read the latest entries in the Notion **journal**.

## Version control (git + GitHub)
- Public repo `https://github.com/BjoernGit/ghostcut`, default branch `main`.
- **Code:** small changes directly on `main`, larger ones on `feature/<name>`. Merge into `main`
  only on Björn's go (see memory "Working with Björn"). Stay on a feature branch until it is
  merged; one branch may carry several tickets.
- **Docs and memory** (root `*.md`, `memory/`) may always go directly to `main` — as a separate
  commit containing only those files. Never leave memory sitting only on a feature branch.
- Commit with **explicit paths**, read `git status` first. After the commit, `git status --short`
  must be empty or show only known items.
- The commit email is the GitHub noreply address (set repo-locally) — do not change it.
- **No agent attribution** in commits (no `Co-Authored-By` lines, no "Generated with").
- Push after every commit unless Björn says otherwise.

## Public means: nothing private (critical)
- **Never in the repo:** game footage, voice lines, exports (unreleased material), Ghostcut
  project files (absolute local paths), Whisper models, API keys, `.env`, local MCP/agent
  configuration (`.claude/settings.local.json`, `.mcp.json`). Test material lives in `media/`
  (ignored).
- **Never in code, docs or memory:** email addresses, real names other than "Björn", user paths
  (`C:\Users\…`), tokens, Notion content from other projects.
- If `git status` shows a media file or anything secret-like: do not commit, extend `.gitignore`.
- A test that needs real media generates it at runtime (e.g. ffmpeg `testsrc`/`sine`).
- GitHub secret scanning + push protection are on. A blocked push means: remove the secret from
  the history, do not bypass.
- Whisper runs **locally** — no cloud service, no key.

## Notion
- **Hub:** https://www.notion.so/419b7847b9dd81198db0c5c16a9e3df4 ("Ghostcut — Video Editor Hub")
- **TODOs:** `collection://419b7847-b9dd-81d1-a552-000b0ec34c8f`, IDs `GC-N`.
- **Journal:** https://www.notion.so/419b7847b9dd81538ff1cdbe068b7075 (writing rules at the top of
  the page, newest entry on top).
- Notion content is written in **German** (Björn reads it there).

### Project scope — ONLY the Ghostcut hub (critical)
The Notion workspace contains other projects, among them **"Lua - Coding Game"** (with its own
TODOs database, journal, notes and "Rechner-Status") and **"VRNinja"** (with a "Bug Tracker").
Their results **do not exist** for Ghostcut — never read, write or cite them.
- Always call `notion-search` with `page_url: "419b7847b9dd81198db0c5c16a9e3df4"`.
- Before any write or reference, check the `<ancestor-path>`: it must end at the Ghostcut hub.
  **IDs look the same across all projects** (`…7847b9dd…`) — only the path tells where a page
  belongs.
- Tickets go exclusively into the Ghostcut TODOs above. A database called "TODOs" without a
  Ghostcut path belongs to another project.
- Resolve `GC-N` via SQL on `userDefined:ID`, never search the bare number semantically
  (recipe in memory).

### Ticket rules
- Start: Status = "In Progress". On abort, back to the previous status.
- Finished: "Review" when Björn should check something; "Done" after his sign-off. A merge order
  **is** the sign-off: set "Done" first, then merge, then write the journal entry.
- Reject via **"Won't Do"** — never delete.
- **No unrequested tickets or doc pages.** Raise follow-up ideas in the chat.
- **Inbox:** concrete observations Björn mentions in passing (a bug, "fühlt sich komisch an",
  "das fehlt") are noted by the agent **without asking** — **one entry per conversation**: title
  `Notizen YYYY-MM-DD — <Thema>`, status "Inbox", each observation one bullet with his quote, no
  elaboration. Not for the inbox: questions, anything done in the same session, anything that
  already has a ticket (add it there). His shortcuts: "ins Inbox" / "nicht notieren". Every reply
  that noted something ends with `→ Inbox: <title> (+N)`. Agents never pick up inbox entries on
  their own; Björn and an agent cut tickets from them together, then the entry goes to
  "Won't Do" with links to the tickets.
- When a change invalidates a statement on the hub or in a ticket: correct it there.

### Knowledge belongs in Notion
**The chat is not storage.** Design, decisions, how a system works and measurements belong in
Notion (a hub subpage or a ticket) and are kept current there. Finished work goes into the
journal. Only what the Memory section allows goes into memory.

## Memory (`memory/` in the repo)
- Agent tooling: tool pitfalls, recipes, working rules — **no project knowledge** (that is
  Notion) and nothing readable from the code.
- `memory/MEMORY.md` is the index (one line per note, no content); single files carry frontmatter
  `name`, `description`, `metadata.type` (`user | feedback | project | reference`), links as
  `[[name]]`.
- **New memory only with Björn's OK** — ask briefly first. Correct existing notes instead of
  creating duplicates; delete wrong ones.
- Public: same rules as above (no personal data, no user paths).
- Commit and push changes right away, directly on `main`.
- **Claude Code setup** (once per machine, not in the repo): in `.claude/settings.local.json` set
  `"autoMemoryDirectory"` to the absolute path of `memory/`. Claude Code then loads the index
  automatically and writes notes there. The hook warns when it is missing.

## Working with Björn
- Talk to Björn in **German**.
- "Wäre es möglich…?" / "Könnte man…?" are **feasibility questions**, not orders. Discuss first,
  implement on an explicit go. After the go, decide implementation details yourself and name
  them as assumptions (memory "Working with Björn").
- **Björn tests himself** how cutting, preview and handling feel. Agents check mechanics (does it
  run, is the output right, does it throw) — e.g. via an app screenshot or ffprobe on an export.
  A screenshot does not replace Björn's test.
- Say briefly what is happening instead of working silently. Longer work ends with a TL;DR.
- Questions as plain text, one at a time, no multiple-choice prompts.

## Code style
- Comments, identifiers and commit messages in English. UI texts in English for now.

## Architecture
- **Quality over quick-and-dirty**, the project should be able to grow: clean separation of
  timeline model, rendering/preview, audio and export; tunable values instead of magic numbers.
  Shortcuts only with Björn's consent and a follow-up ticket.
- If a better architecture becomes apparent while working: propose it briefly (problem, better
  structure, trade-off), then decide together — implement right away or record it as a ticket.
  **Never leave it unmentioned.**
