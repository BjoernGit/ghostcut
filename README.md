# Ghostcut

A small video editor built for cutting game trailers and devlogs.

**The idea:** in a regular editor, trimming a clip hides what you cut away. Ghostcut keeps it visible.

- Clips sit on one alignment line and alternate between two lanes. Each new clip starts exactly at the cut edge of the previous one, offset one lane down.
- The trimmed-off parts stay in place as greyed-out "ghosts" that overhang the cut, so you always see how much you removed and can pull it back.
- A second track holds voice lines that overlay the game footage.
- Spoken text is transcribed locally with Whisper and can be burned into the video as real text.

## Status

Early planning. Nothing to run yet.

## Planned

- Drag-and-drop import of clips (video + game audio)
- Ghost timeline (alternating lanes, visible trims)
- Voice-line track with recording
- Local speech-to-text (Whisper) → editable captions in the video
- Voice processing (compressor, EQ, noise gate, …)
- Export in common qualities (e.g. Steam trailer presets)

## Privacy

This repo is public. Footage, audio, project files and models are never committed — see `.gitignore`.
