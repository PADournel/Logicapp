# Logicapp

*by OnTrack Software*

A lightweight Windows 11 app that adds **collapsible zones** to your desktop for organizing icons — a small, dependency-free alternative to tools like Fences.

## Features

- **Zones on the desktop**: drag icons from the desktop into a zone; drag them out to put them back. Ctrl while dragging copies instead of moving.
- **Collapsible**: click a zone's title to roll it up to its title bar (with an item count); hover a collapsed zone to peek inside.
- **Fades when idle**: when the mouse leaves a zone, the whole zone quickly fades to 40% of its normal opacity (adjustable per zone, 100% disables it), and fades back in on hover.
- **Sorting** (always on a grid): by name, by creation date (newest first), or free — drag an icon inside its zone to place it; the order is saved. "Reverse order" is available for name/date.
- **Gamer style presets** with cyberpunk cut corners and RGB animations:
  - **Néon RGB** (default): dark glass, rotating rainbow border, scrolling RGB line under the title
  - **Night City**: cyan beveled title tab, magenta accents, scanlines, a sweeping light beam
  - **Studio**: sober dark card with an accent line that slowly shifts color
  - **Classique**: the original rounded look
- **Illustrated presets**, each with subtle hover animations:
  - **Rallye Stellaire**: brushed gunmetal, orange racing chevrons, a cyan light blade that ignites under the title, holographic grid and scan
  - **Friche**: stained concrete with moss and a vine, name handwritten on masking tape, a flashlight beam that follows the mouse, drifting spores
  - **Sanguine**: gothic bronze frame with corner ornaments, Roman numeral count, a line of blood that spreads and drips, candlelight and mist
  - **Veillée**: stone tome with a gold serif tab, recessed sepia icon slots, filigree rules, a glint along the frame, candlelight and mist
  - **Circuit**: cream paper, "/NAME" title, retro racing stripes, teal icon cards that lift on hover, speed streaks and blowing sand
  - **Exil**: charcoal holo-map with copper orbits, spaced capitals with a stretching underline, round icon discs, white name pill, orbiting dots
  - Animations run smoothly only while the mouse is over a zone (or very slowly in "always" mode), so the app stays at ~0% CPU when idle
- **Appearance settings** (gear icon in each title bar), with live preview:
  - style preset, RGB animation (off / on hover / always, slow)
  - zone name, corner radius or cut size, border width / color / opacity
  - background color with adjustable opacity, or a background image (fill, fit, stretch, tile, center) with its own opacity
  - title font, text color, bar color / opacity, alignment, item count when collapsed
  - icon size (16–128 px), spacing, label font / color, label shadow
  - "Apply to all zones" (also becomes the default for new zones)
- **Per-pixel transparency**: only the background is translucent — icons and text stay crisp.
- **Stays on the desktop**: sits just above the wallpaper, below all windows, and remains visible with Win+D.
- **Starts with Windows** (toggle in the tray menu) and restores every zone, position, and style.
- **Light on resources**: ~40 KB executable, a few MB of RAM, 0% CPU when idle (event-driven, no polling).

## How it works

- Each zone is a real folder in `Zones du bureau`, next to your Desktop folder (inside OneDrive if your Desktop is synced). Nothing is lost if the app is removed: your files stay in those folders.
- Settings are saved in `Zones du bureau\zones.cfg` (plain INI-style text) and reloaded at startup.
- Background images are copied into the hidden `Zones du bureau\.fonds` folder.
- Errors never close the app; they are written to `erreurs.log` next to the executable.

## Build

No SDK required — it uses the C# compiler that ships with Windows (.NET Framework 4.8):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

This generates the icon and produces `Logicapp.exe` in the repository folder. Note: the script stops any running instance before compiling.

## Usage

- **Title click**: collapse / expand. **Title drag**: move. **Edges**: resize.
- **Gear icon**: appearance settings.
- **Right-click**: new zone, rename, delete zone (its content goes back to the desktop), lock zones.
- **Items**: double-click to open, F2 to rename, Del to send to the Recycle Bin, Ctrl+A to select all, rubber-band selection with the mouse.
- **Tray icon**: new zone, lock, peek on hover, start with Windows, quit.

## Requirements

Windows 10/11 with .NET Framework 4.8 (included in Windows).
