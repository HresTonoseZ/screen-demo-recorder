# Screen Demo Recorder

Screen Demo Recorder is a compact Windows desktop application for recording a
complete monitor or a selected region and turning the result into a polished,
captioned animated GIF. It supports Windows 10 version 2004 and newer and
Windows 11.

A staged C#/.NET 10 rewrite is also available. The native preview records a
display, region, or individual window to MP4 and exports GIF with labels and
optional pressed-key and mouse-click overlays. It can keep the selected area
boundary visible and show the label, pressed keys, and click highlights live on
the desktop without intercepting input. It includes configurable global shortcuts,
pause/resume, countdown, MP4/GIF resolution presets, automatic encoder fallback,
export progress/cancellation, safe saving, profile transfer, recent recordings,
themes and notification-area behavior. The feature list below describes the Python
version; see [native progress](docs/NATIVE_PROGRESS.md) for the current scope.

## Features

- Records any connected monitor or a precise region on one monitor.
- Provides four draggable region points connected by a configurable dashed
  line, with optional outside dimming and live dimensions.
- Excludes the application window, region selector, countdown, and caption
  preview from the source recording through the Windows display-affinity API.
- Keeps a small normal window on top when requested and supports standard
  minimize, close, and optional notification-area behavior.
- Starts, stops, pauses, resumes, or cancels recording through configurable
  global hotkeys.
- Captures the mouse cursor when requested.
- Supports a countdown and a maximum recording duration.
- Saves GIFs to a user-selected folder without overwriting an existing file.
- Optionally keeps the source MP4 for later editing.
- Renders the caption preview with the same Pillow renderer used for final GIF
  frames, so its size and placement match post-processing.
- Stores multiple named profiles and supports validated JSON import and export.
- Includes dark, light, and system themes, a recent-GIF list, completion
  notifications, a single-instance lock, and an error log that never records
  screen contents.

## Install from source

Install Python 3.10 or newer, then run:

```powershell
python -m pip install -e .
screen-demo-recorder
```

The application dependencies are PySide6, Pillow, MSS, pynput, and
imageio-ffmpeg. FFmpeg is supplied by imageio-ffmpeg; a separate system FFmpeg
installation is not required.

## Record a GIF

1. Choose **Full monitor** or **Selected region** on the **Capture** tab.
2. Select the monitor. For a region, press **Select Region**, drag any corner
   point or move the complete rectangle, then press `Enter`. Press `Esc` to
   cancel the selector.
3. Choose the GIF folder and encoding settings on the **Output** tab.
4. Configure the title, subtitle, badge, container, colors, type, border,
   shadows, and placement on the **Caption** tab.
5. Press **Show Preview** to inspect the exact overlay. The preview may remain
   visible while recording because it is excluded from source capture and is
   added during post-processing.
6. Press **Record** or the record hotkey. Press it again to stop.

The default global hotkeys are:

| Action | Hotkey |
| --- | --- |
| Record / Stop | `Ctrl+Shift+F9` |
| Pause / Resume | `Ctrl+Shift+F8` |
| Cancel | `Ctrl+Shift+F10` |

Hotkey fields use pynput syntax, such as `<ctrl>+<shift>+<f9>`. The three
combinations must be different.

## Capture settings

The **Capture** tab controls:

- source monitor and full-monitor or region mode;
- manual region X, Y, width, and height;
- optional aspect-ratio locking, edge snapping, and minimum region size;
- source and GIF frame rates;
- cursor capture;
- countdown from 0 to 10 seconds;
- maximum duration, where `0` disables the limit;
- record, pause, and cancel hotkeys;
- always-on-top, close-to-tray, and theme behavior.

Monitor coordinates are physical and support displays positioned left of or
above the primary monitor. A saved region is relative to its selected monitor,
so it remains understandable when that monitor moves in the virtual desktop.

## Output and GIF settings

The output filename template accepts these placeholders:

- `{date}` as `YYYY-MM-DD`;
- `{time}` as `HH-MM-SS`;
- `{title}` as a filename-safe title;
- `{counter}` as a non-overwriting sequence number.

The application always writes the GIF to a temporary sibling first and then
atomically replaces the final destination. Existing output is never
overwritten. GIF controls include width, palette size, dithering, loop count,
frame sampling, final-frame duration, source-video retention, and opening the
output folder after completion.

## Caption and badge

The caption supports nine anchors, exact offsets, width, padding, line spacing,
text alignment, background color and transparency, blur, rounded corners,
borders, and shadows. Title and subtitle settings are independent and include
text, font file, size, bold, italic, color, outline, and shadow controls.

The badge supports independent text, font, colors, transparency, fixed or
automatic dimensions, six inside/above positions, padding, border, rounded
corners, shadows, and X/Y offsets.

Colors use `#RRGGBB` or `#RRGGBBAA` notation. A blank font path uses Segoe UI
with an Arial fallback. A custom font must be a readable TrueType or OpenType
file.

## Selection appearance

The **Selection** tab controls line color, alpha, width, dash length and gap,
point color, point border, point size, point shape, outside dimming, and the
dimension label. Selection UI is never composited into the GIF.

## Profiles and files

Settings are stored at:

```text
<ScreenDemoRecorder.exe folder>\settings-v2.json
```

The native application is portable: profiles, presets, recent-file history,
and application settings stay beside the executable. Existing native settings
from `%LOCALAPPDATA%\Screen Demo Recorder` are moved here on the first launch.
Profiles may be saved, deleted, reset, exported, and imported from JSON.
Imports are fully validated before replacing active settings.

## Build the executable

Run the one-command build from PowerShell:

```powershell
.\build.ps1
```

The script creates an isolated `.venv-build`, installs the declared build
requirements, runs the complete test suite, generates the application icon and
Windows version resource, produces an `onedir` diagnostic build, smoke-tests
it, and then creates:

```text
dist\ScreenDemoRecorder.exe
```

Build only the diagnostic folder when troubleshooting packaging:

```powershell
.\build.ps1 -Package onedir
```

If Python is not registered as `py` or `python`, pass the executable directly:

```powershell
.\build.ps1 -Python "C:\Path\To\python.exe"
```

The executable is self-contained and does not require Python on the target
computer. It is not digitally signed, so Windows SmartScreen may warn on first
launch until a trusted code-signing certificate is added to the release
process.

## Technical notes and limitations

- Animated GIF does not support audio. Retain the source MP4 when audio or a
  video editor is required later.
- Windows display affinity is designed to remove owned control windows from
  supported public capture APIs. It is not a digital-rights-management or
  anti-photography security boundary.
- Disconnecting the selected monitor, changing display topology, locking the
  desktop, exhausting storage, or losing write access may stop the recording.
  Failed post-processing keeps the temporary MP4 and reports its recovery path.
- Large dimensions, high GIF frame rates, long durations, and 256-color
  palettes increase processing time and memory use. The default 60-second
  recording limit prevents accidental unbounded sessions.

## Development

The native rewrite lives under `native/` and targets .NET 10 WPF. It currently
contains the compact recorder shell, profile persistence and legacy migration,
the native region and searchable window selectors, persistent capture-excluded
region boundary, universal label editor with matching MP4 composition, pressed-key
and animated mouse-click overlays, profile import/export/reset, recent-file access, theme/tray behavior,
selection-appearance presets, and streaming GIF export using Windows Media Foundation and WIC. GIF size, frame rate,
palette, dithering, repeat, frame sampling, last-frame duration and MP4 retention
are stored per profile. MP4 output width and quality are profile-specific too;
its filename template and open-folder behavior are editable alongside the resolution.
Aspect ratio is preserved and smaller captures are not enlarged. The legacy badge
is intentionally omitted from the native profile schema. Label rows can be selected and edited directly on the exact-size
canvas; container colors, padding, spacing, corners, border and shadow remain in
a collapsed advanced section, while each row exposes exact text, outline and shadow
controls. GPU background blur is available for glass labels. Pressed-key opacity and
timing, plus exact left/right click colors, stay in their own collapsed Advanced sections.
Per-Monitor V2 coordinates are kept in physical pixels; the build checks every
connected display against Windows Graphics Capture and verifies synthetic
100–200% viewport mappings, including monitors with negative desktop origins.

For a one-click self-contained build, double-click `build-native.bat`. It checks
for the required .NET SDK and offers to install a private current-user copy when
needed. Before publishing, it stops build servers, closes only a previous preview
running from `dist\native-preview`, removes that old output folder completely,
and creates a clean replacement. Source files, profiles, and recordings are not
deleted.

Build and verify the self-contained preview with `.\build-native.ps1`, then run
`dist\native-preview\ScreenDemoRecorder.exe`. Keep its adjacent files together.
MP4 capture and GIF export are connected; see [native progress](docs/NATIVE_PROGRESS.md)
for implemented behavior and remaining work.

Build the current-user MSI with `.\build-installer.ps1`. It installs without an
administrator prompt under `%LOCALAPPDATA%\Programs\Screen Demo Recorder`, adds a
Start Menu shortcut, and supports standard Windows uninstall and major upgrades.
The build validates the MSI database automatically. Add `-VerifyPackage` to create
an administrative image under `build/` and compare every packaged file byte-for-byte
with the self-contained preview. The development MSI is not code-signed yet, so
Windows may show an unknown-publisher warning.

Build the native solution and run its dependency-free core checks with:

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build native\ScreenDemoRecorder.sln
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" run --project native\tests\ScreenDemoRecorder.CoreChecks
```

The UX contract for the rewrite is in [`docs/UX_REDESIGN.md`](docs/UX_REDESIGN.md),
and the completed source-level comparison is in
[`docs/NATIVE_PARITY.md`](docs/NATIVE_PARITY.md).

### Python implementation

Run the host-independent tests with:

```powershell
python -m unittest discover -s tests -v
```

The capture loop lives in `screen_demo_recorder/recording.py`, Windows monitor
and display-affinity operations in `screen_demo_recorder/windows.py`, shared
caption rendering in `screen_demo_recorder/render.py`, GIF processing in
`screen_demo_recorder/pipeline.py`, persistent profiles in
`screen_demo_recorder/settings.py`, and the desktop interface in
`screen_demo_recorder/ui.py`.

## Project

- Version: `1.0.2`
- Maintainer: HresTonoseZ
- Minimum platform: Windows 10 version 2004
- License: [MIT](LICENSE)
