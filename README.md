# Ghostcut

A small video editor built for cutting game trailers and devlogs.

**The idea:** in a regular editor, trimming a clip hides what you cut away. Ghostcut keeps it visible.

- Clips sit on one alignment line and alternate between two lanes. Each new clip starts exactly at the cut edge of the previous one, offset one lane down.
- The trimmed-off parts stay in place as greyed-out "ghosts" that overhang the cut, so you always see how much you removed and can pull it back.
- A second track holds voice lines that overlay the game footage.
- Spoken text is transcribed locally with Whisper and can be burned into the video as real text.

## Status

First usable version: import, ghost timeline, voice/music tracks, preview, undo/redo, save/load and MP4 export.

- Drag-and-drop import of video and audio files
- Ghost chain: clips alternate between lanes, trimmed parts stay as greyed-out overhangs, ripple is always on, reorder by dragging, split at the playhead, snapping with a visible line
- Voice and music tracks with freely placed clips, per-clip volume, track mute
- Automatic anti-click fades at every audio edge (10 ms, crossfading from overhang material at cuts)
- Fade from/to black per clip
- Preview with synced sound and scrubbing
- Projects as readable JSON (`.ghostcut`), autosave
- MP4 export (H.264 + AAC) with presets: Steam trailer (1080p60), 1080p60, 1080p30, draft 720p30

## Planned

- Transitions between video clips, title cards
- Voice-line recording and voice processing (compressor, EQ, noise gate, …)
- Local speech-to-text (Whisper) → editable captions in the video

## Running

Needs the .NET 10 SDK and ffmpeg (with ffprobe). Ghostcut finds ffmpeg on `PATH`, in the winget package
folder, or in the folder named by `GHOSTCUT_FFMPEG`.

```
winget install --id Gyan.FFmpeg.Essentials -e
dotnet run --project src/Ghostcut.App
dotnet test
```

Caches and the autosave live in `%LOCALAPPDATA%\Ghostcut` (override with `GHOSTCUT_DATA_DIR`).

## Layout

- `src/Ghostcut.Core` — timeline model, edit commands (undo/redo), project format, audio mix. No UI.
- `src/Ghostcut.Media` — ffmpeg/ffprobe: probing, audio decoding cache, preview frames, export.
- `src/Ghostcut.App` — Avalonia desktop app.
- `tests/` — model tests, export integration tests and a headless UI test (the latter two generate their media with ffmpeg and are skipped without it).

## Privacy

This repo is public. Footage, audio, project files and models are never committed — see `.gitignore`.
