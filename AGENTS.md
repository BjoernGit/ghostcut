# Ghostcut — Agent Project Context

Diese Datei ist die **einzige Quelle** für Projektkonventionen, unabhängig vom Tool
(Claude Code, Codex, …). `CLAUDE.md` importiert sie nur. Hier steht nur, was aus dem Code
**nicht** ablesbar ist.

## Projekt
Eigenes Schnittprogramm für Trailer und Devlogs des **Lua Coding Game** (Auslöser: Steam-Trailer).
Kernidee: die **Ghost-Timeline** — Clips wechseln auf einer Ausrichtungslinie zwischen zwei
Spuren, jeder neue Clip setzt an der Schnittkante des vorherigen an, und weggeschnittene Stücke
bleiben ausgegraut als Überhang sichtbar. Dazu eine Voice-Spur, Whisper-Transkription
(lokal) als echter Text im Video, Voice-Bearbeitung und Export.

## Tech-Stack (GC-1)
- **C#/.NET 10 + Avalonia**, Windows zuerst, nichts Windows-Exklusives ohne Not.
- Timeline ist ein eigenes, frei gezeichnetes Control — kein Standard-Widget.
- Vorschau: Frames per ffmpeg-Bindings dekodieren und selbst rendern; Proxies als Ausweichweg.
- Export über ffmpeg. Speech-to-Text über Whisper.net (whisper.cpp), lokal. Audio über NAudio.
- Begründung und Annahmen stehen in GC-1.

Ghostcut ist ein **eigenständiges Projekt**. Die Regeln des Lua-Repos (Plastic, Unity, MCP-Ports,
Auto-Agents, Lua-Memory) gelten hier **nicht**.

## Versionierung
- git, öffentliches Repo `https://github.com/BjoernGit/ghostcut`, Default-Branch `main`.
- Kleine Sachen direkt auf `main`, größere auf Feature-Branches (`feature/<name>`), Merge auf
  Björns Go.
- Commits mit expliziten Pfaden, kein `git add -A` ohne vorheriges `git status`.
- Commit-Mail im Repo ist die GitHub-noreply-Adresse (lokal gesetzt) — nicht ändern.

## Öffentlich heißt: nichts Privates (kritisch)
- **Nie ins Repo:** Spielaufnahmen, Voice-Lines, Exporte (unveröffentlichtes Material), Ghostcut-
  Projektdateien (absolute lokale Pfade), Whisper-Modelle, API-Keys, `.env`, lokale
  MCP-/Agent-Konfiguration. Testmaterial liegt in `media/` (ignoriert).
- Vor jedem Commit `git status` lesen. Taucht eine Mediendatei oder etwas Geheimnisartiges auf:
  nicht committen, `.gitignore` erweitern.
- Braucht ein Test echte Mediendaten, erzeugt er sie zur Laufzeit (z. B. ffmpeg `testsrc`/`sine`).
- GitHub Secret Scanning + Push Protection sind an. Ein geblockter Push heißt: Secret entfernen,
  nicht umgehen.
- Whisper läuft **lokal** — kein Cloud-Dienst, kein Key.

## Notion
- **Hub:** https://www.notion.so/419b7847b9dd81198db0c5c16a9e3df4 („Ghostcut — Video Editor Hub“)
- **TODOs:** `collection://419b7847-b9dd-81d1-a552-000b0ec34c8f`, IDs `GC-N`.
- Scope: nur dieser Hub. Lua-Hub und VRNinja sind für dieses Projekt nicht existent; Ghostcut-
  Tickets nie in die Lua-TODOs.
- Design, Entscheidungen und Messwerte gehören nach Notion (Hub oder Ticket), nicht in den Chat.
- Ticket-Regeln wie im Lua-Projekt: Start = Status „In Progress“; Ablehnen über „Won't Do“, nie
  löschen; keine unaufgeforderten Tickets; Björns nebenbei genannte Beobachtungen als **ein**
  Inbox-Eintrag pro Gespräch (`Notizen YYYY-MM-DD — <Thema>`, Status „Inbox“, seine Worte).

## Zusammenarbeit mit Björn
- „Wäre es möglich…?“ ist eine Machbarkeitsfrage, kein Auftrag.
- Björn testet selbst, wie sich Schnitt und Bedienung anfühlen.
- Chat-Titel: `GC-<Thema>`.
- Rückfragen im Fließtext, keine Multiple-Choice-Abfragen.

## Code Style
- Kommentare und Bezeichner auf Englisch. UI-Texte vorerst Englisch.
- Qualität vor Quick-and-dirty; bessere Architektur kurz vorschlagen, gemeinsam entscheiden.
