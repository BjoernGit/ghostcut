#!/usr/bin/env bash
# SessionStart hook (Claude Code, .claude/settings.json); any other agent can run it by hand.
# Fast-forwards main when the working tree is clean, reports branch and pending changes, and
# checks that Claude Code's auto memory points at memory/. Prints a short status block that the
# agent reads as context. Never fails the session start.

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root" || exit 0

echo "Session start check (tools/agent/session-start.sh):"

branch="$(git rev-parse --abbrev-ref HEAD 2>/dev/null)"
if [ -z "$branch" ]; then
  echo "- git: not a repository?"
  exit 0
fi

pending="$(git status --porcelain 2>/dev/null | grep -c .)"

if [ "$pending" -eq 0 ]; then
  if out="$(timeout 30 git pull --ff-only -q 2>&1)"; then
    echo "- git: on $branch, pulled ($(git log -1 --format='%h %cr: %s'))."
  else
    echo "- git: on $branch, PULL FAILED - resolve before working: ${out//$'\n'/ }"
  fi
else
  timeout 30 git fetch -q 2>/dev/null
  echo "- git: on $branch, $pending pending change(s) - not pulled. Check them before working:"
  git status --short 2>/dev/null | head -10 | sed 's/^/    /'
fi

counts="$(git rev-list --left-right --count '@{upstream}...HEAD' 2>/dev/null)"
if [ -n "$counts" ]; then
  behind="${counts%%[[:space:]]*}"
  ahead="${counts##*[[:space:]]}"
  [ "$behind" != "0" ] && echo "- Branch is $behind commit(s) BEHIND its upstream."
  [ "$ahead" != "0" ] && echo "- Branch is $ahead commit(s) ahead of its upstream (unpushed)."
fi

if [ "$branch" != "main" ]; then
  echo "- On feature branch $branch: stay here until it is merged (AGENTS.md, Versionierung)."
fi

if ! grep -q '"autoMemoryDirectory"' "$root/.claude/settings.local.json" 2>/dev/null; then
  echo "- Claude Code auto memory not set up: add \"autoMemoryDirectory\" (absolute path to memory/) to .claude/settings.local.json (AGENTS.md, Memory). Read memory/MEMORY.md by hand meanwhile."
fi

echo "- Before ticket work: read the latest Notion journal entries (AGENTS.md, Notion)."
exit 0
