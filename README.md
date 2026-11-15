# GlitterVis

A modern full-screen music visualizer for MINI's TV. It listens to whatever MINI plays (Winamp or anything else,
via WASAPI loopback), so it needs no Winamp plugin to work. It shows seven GPU scenes and crossfades on a beat
about every 40 seconds, and on every new track. Colors come from the album cover. A title card fades in for each new track.

Scenes: nebula, liquid, aurora, tunnel, galaxy, horizon (mirrored spectrum over water), kaleido.

## Keys
N or Right = next scene · Left = previous · L = lock this scene · C = calm mode (slower, softer) · T = show title · Esc or Q = quit

## Options
`--window` (windowed) · `--height 720` (lighter render if the GPU struggles; default 1080) · `--snapshot <folder>` (render every scene to PNG and exit)

## Where things are
- Source: `src/` (.NET 8, OpenTK, NAudio). Shaders: `src/shaders/*.frag` + `common.glsl`. Edit a .frag and copy it to MINI; no rebuild needed.
- Build: `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../publish`
- MINI: `C:\MediaTools\GlitterVis\` (exe + shaders), shortcut on the Public desktop. Log: `glittervis.log` beside the exe (fps every 30 s).
- Track info: Winamp window title + IPC for the file path; cover = folder.jpg / cover.jpg / front.jpg / AlbumArt*.jpg / largest jpg in the folder.

## Phase 2 (not built)
- Winamp launcher: a small vis plugin so it shows in Winamp's visualization menu and launches GlitterVis.
- MoBetter buttons (start, next, lock, calm) through a local control port.
