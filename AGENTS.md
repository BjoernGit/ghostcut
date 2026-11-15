# Ghostcut — Agent Project Context

Diese Datei ist die **einzige Quelle** für Projektkonventionen und gilt für jeden Agent,
unabhängig vom Tool (Claude Code, Codex, …). `CLAUDE.md` importiert sie nur. Sie ist
**vollständig** — ein Agent braucht kein Wissen aus anderen Projekten. Hier steht nur, was aus
dem Code nicht ablesbar ist; Rezepte und Fallstricke stehen im Memory (`memory/MEMORY.md`).

## Projekt
Eigenes Schnittprogramm für Trailer und Devlogs von Björns Spiel (Auslöser: Steam-Trailer).
Kernidee ist die **Ghost-Timeline**: Clips wechseln auf einer Ausrichtungslinie zwischen zwei
Spuren, jeder neue Clip setzt mit seiner Anfangskante an der Schnittkante des vorherigen an, und
weggeschnittene Stücke bleiben ausgegraut als Überhang sichtbar. Dazu kommen eine Voice-Spur,
Whisper-Transkription (lokal) als echter Text im Video, Voice-Bearbeitung (Kompressor, EQ, …)
und Export in gängigen Qualitäten.

## Tech-Stack (GC-1)
- **C#/.NET 10 + Avalonia**, Windows zuerst, nichts Windows-Exklusives ohne Not.
- Timeline ist ein eigenes, frei gezeichnetes Control — kein Standard-Widget.
- Vorschau: Frames per ffmpeg-Bindings dekodieren und selbst rendern; Proxies als Ausweichweg.
- Export über ffmpeg. Speech-to-Text über Whisper.net (whisper.cpp), lokal. Audio über NAudio.
- Begründung und Annahmen stehen in GC-1.

## Sessionbeginn (Pflicht)
1. Chat-Titel `GC-<Thema>` setzen, sobald das Thema aus Björns erster Nachricht klar ist
   (Claude Desktop: `set_session_title`; kann das Tool keine Titel setzen, entfällt es).
2. Der SessionStart-Hook (`tools/agent/session-start.sh`) holt `main` per `git pull --ff-only`,
   wenn der Arbeitsbaum sauber ist, und meldet Branch und offene Änderungen. Andere Agents rufen
   ihn von Hand auf.
3. `memory/MEMORY.md` lesen (Claude Code lädt ihn automatisch, wenn eingerichtet — siehe Memory).
4. Vor Ticketarbeit die obersten Einträge im Notion-**Journal** lesen.

## Versionierung (git + GitHub)
- Öffentliches Repo `https://github.com/BjoernGit/ghostcut`, Default-Branch `main`.
- **Code:** kleine Sachen direkt auf `main`, größere auf `feature/<name>`. Merge nach `main`
  nur auf Björns Go (siehe Memory „Working with Björn“). Auf einem Feature-Branch bleiben, bis er
  gemerged ist; ein Branch darf mehrere Tickets tragen.
- **Doku und Memory** (`*.md` im Root, `memory/`) dürfen immer direkt auf `main` — als eigener
  Commit, der nur diese Dateien enthält. Memory nie nur auf einem Feature-Branch liegen lassen.
- Commits mit **expliziten Pfaden**, vorher `git status` lesen. Nach dem Commit muss
  `git status --short` leer sein oder nur Bekanntes zeigen.
- Commit-Mail ist die GitHub-noreply-Adresse (repo-lokal gesetzt) — nicht ändern.
- **Keine Agent-Attribution** in Commits (keine `Co-Authored-By`-Zeilen, kein „Generated with“).
- Gepusht wird nach jedem Commit, sofern Björn nichts anderes sagt.

## Öffentlich heißt: nichts Privates (kritisch)
- **Nie ins Repo:** Spielaufnahmen, Voice-Lines, Exporte (unveröffentlichtes Material),
  Ghostcut-Projektdateien (absolute lokale Pfade), Whisper-Modelle, API-Keys, `.env`, lokale
  MCP-/Agent-Konfiguration (`.claude/settings.local.json`, `.mcp.json`). Testmaterial liegt in
  `media/` (ignoriert).
- **Nie in Code, Doku oder Memory:** Mailadressen, Klarnamen außer „Björn“, Benutzerpfade
  (`C:\Users\…`), Tokens, Notion-Inhalte aus anderen Projekten.
- Taucht im `git status` eine Mediendatei oder etwas Geheimnisartiges auf: nicht committen,
  `.gitignore` erweitern.
- Braucht ein Test echte Mediendaten, erzeugt er sie zur Laufzeit (z. B. ffmpeg `testsrc`/`sine`).
- GitHub Secret Scanning + Push Protection sind an. Ein geblockter Push heißt: Secret aus der
  History entfernen, nicht umgehen.
- Whisper läuft **lokal** — kein Cloud-Dienst, kein Key.

## Notion
- **Hub:** https://www.notion.so/419b7847b9dd81198db0c5c16a9e3df4 („Ghostcut — Video Editor Hub“)
- **TODOs:** `collection://419b7847-b9dd-81d1-a552-000b0ec34c8f`, IDs `GC-N`.
- **Journal:** https://www.notion.so/419b7847b9dd81538ff1cdbe068b7075 (Schreibregeln oben auf der
  Seite, neuester Eintrag oben).

### Projekt-Scope — NUR der Ghostcut-Hub (kritisch)
Der Notion-Workspace enthält weitere Projekte, u. a. **„Lua - Coding Game“** (mit eigener
TODOs-DB, Journal, Notes und „Rechner-Status“) und **„VRNinja“** (mit „Bug Tracker“). Deren
Treffer sind für Ghostcut **nicht existent** — nie lesen, nie schreiben, nie zitieren.
- `notion-search` immer mit `page_url: "419b7847b9dd81198db0c5c16a9e3df4"`.
- Vor jedem Schreib- oder Referenzzugriff den `<ancestor-path>` prüfen: Er muss beim
  Ghostcut-Hub enden. **Die IDs sehen in allen Projekten gleich aus** (`…7847b9dd…`) — nur der
  Pfad zeigt, wohin eine Seite gehört.
- Tickets gehören ausschließlich in die Ghostcut-TODOs oben. Eine DB namens „TODOs“ ohne
  Ghostcut-Pfad gehört zu einem anderen Projekt.
- `GC-N` per SQL auf `userDefined:ID` auflösen, nie die nackte Nummer semantisch suchen
  (Rezept im Memory).

### Ticket-Arbeitsregeln
- Start: Status = „In Progress“. Bei Abbruch zurück auf den vorherigen Status.
- Fertig: „Review“, wenn Björn etwas prüfen soll; „Done“ nach seiner Abnahme. Ein Merge-Auftrag
  **ist** die Abnahme: zuerst „Done“, dann mergen, dann Journal-Eintrag.
- Ablehnen über **„Won't Do“** — nie löschen.
- **Keine unaufgeforderten Tickets oder Doc-Seiten.** Folgeideen im Chat ansprechen.
- **Inbox:** Konkrete Beobachtungen, die Björn nebenbei nennt (Bug, „fühlt sich komisch an“,
  „das fehlt“), notiert der Agent **ohne Rückfrage** — **ein Eintrag pro Gespräch**: Titel
  `Notizen YYYY-MM-DD — <Thema>`, Status „Inbox“, jede Beobachtung ein Stichpunkt mit seinem
  Zitat, keine Ausarbeitung. Nicht in die Inbox: Fragen, was in der Session gleich erledigt
  wird, was schon ein Ticket hat (dort ergänzen). Kurzbefehle: „ins Inbox“ / „nicht notieren“.
  Jede Antwort, die etwas notiert hat, endet mit `→ Inbox: <Titel> (+N)`. Agents nehmen Inbox
  nie selbst; Tickets daraus schneiden Björn und ein Agent gemeinsam, danach geht der Eintrag auf
  „Won't Do“ mit Verweis auf die Tickets.
- Nach einer Änderung, die eine Aussage im Hub oder in einem Ticket ungültig macht: dort
  korrigieren.

### Wissen gehört nach Notion
**Der Chat ist kein Speicher.** Design, Entscheidungen, wie ein System funktioniert und
Messwerte gehören nach Notion (Hub-Unterseite oder Ticket) und werden dort aktuell gehalten.
Abgeschlossene Arbeit kommt ins Journal. Ins Memory nur, was der Abschnitt Memory erlaubt.

## Memory (`memory/` im Repo)
- Agent-Werkzeug: Tool-Fallen, Rezepte, Arbeitsregeln — **kein Projektwissen** (das ist Notion)
  und nichts, was aus dem Code lesbar ist.
- `memory/MEMORY.md` ist der Index (eine Zeile pro Notiz, keine Inhalte); Einzeldateien mit
  Frontmatter `name`, `description`, `metadata.type` (`user | feedback | project | reference`),
  Verweise als `[[name]]`.
- **Neues Memory nur mit Björns OK** — vorher kurz fragen. Bestehende Notizen korrigieren statt
  Dubletten anlegen; falsche löschen.
- Öffentlich: gleiche Regeln wie oben (keine persönlichen Daten, keine Benutzerpfade).
- Änderungen sofort committen und pushen, direkt auf `main`.
- **Claude Code einrichten** (einmal pro Rechner, nicht im Repo): in
  `.claude/settings.local.json` `"autoMemoryDirectory"` auf den absoluten Pfad von `memory/`
  setzen. Dann lädt Claude Code den Index automatisch und schreibt Notizen dorthin. Der Hook
  warnt, wenn das fehlt.

## Zusammenarbeit mit Björn
- „Wäre es möglich…?“ / „Könnte man…?“ sind **Machbarkeitsfragen**, keine Aufträge. Erst
  diskutieren, dann auf explizites Go umsetzen. Details der Umsetzung danach selbst entscheiden
  und als Annahme benennen (Memory „Working with Björn“).
- **Björn testet selbst**, wie sich Schnitt, Vorschau und Bedienung anfühlen. Agents prüfen
  Mechanik (läuft es, stimmt die Ausgabe, wirft es Fehler) — z. B. per Screenshot der App oder
  ffprobe auf einen Export. Ein Screenshot ersetzt nicht Björns Test.
- Kurz sagen, was gerade passiert, statt stumm zu arbeiten. Längere Arbeit endet mit TL;DR.
- Rückfragen im Fließtext, eine Frage auf einmal, keine Multiple-Choice-Abfragen.

## Code Style
- Kommentare, Bezeichner und Commit-Messages auf Englisch. UI-Texte vorerst Englisch.
- Gesprochen wird mit Björn auf Deutsch.

## Architektur
- **Qualität vor Quick-and-dirty**, das Projekt soll wachsen können: klare Trennung von
  Timeline-Modell, Rendering/Vorschau, Audio und Export; designer-tunbare Werte statt
  Magic Numbers. Shortcuts nur mit Björns Zustimmung und Folge-Ticket.
- Fällt beim Arbeiten eine bessere Architektur auf: kurz vorschlagen (Problem, besserer Aufbau,
  Trade-off), dann gemeinsam entscheiden — gleich umsetzen oder als Ticket festhalten.
  **Nie still liegen lassen.**
