# Native implementation status

The native preview is a staged C#/.NET 10 WPF rewrite. It is not yet a replacement
for the Python recorder. No Python source or working executable has been removed.

## Implemented

- Compact recorder settings window with profile selection, duplication, renaming,
  deletion, reset, validated import/export, and debounced saves flushed on close
  and before profile changes. Profile transfer is transactional, rejects unknown
  native fields, and accepts both native v2 exports and legacy v1 exports.
- Versioned native profile file (`settings-v2.json`); first-run migration from
  `settings.json` retains that file and creates `settings-v1.backup.json`.
- Preset-first capture controls in the compact window plus an Advanced capture
  editor for exact recording FPS, countdown, duration, aspect ratio, snapping,
  and profile-specific minimum region size. Every retained v1 profile field is
  exercised by the automated parity contract.
- Universal label model with editable, addable/removable text rows. There is no
  native badge property or mandatory `DEMO` capsule. Enabled legacy title and
  subtitle rows migrate to ordinary text rows; the legacy badge is omitted.
- Labels in MP4 with a shared preview/export renderer: text wrapping, font family
  or legacy `.ttf`/`.otf` file path,
  size, bold/italic, alignment, RGBA colors, text strokes, padding, row spacing,
  rounded backgrounds, borders, GPU backdrop blur, container shadows and independent
  per-row text shadows. A text-only preset removes the panel.
  The rendered label is cached once per recording and composed on the GPU with
  Direct2D; there is no per-frame desktop readback to the CPU.
- Aspect-correct overlay canvas using the selected capture dimensions, nine anchor
  buttons, whole-label dragging, a width handle and width slider. Positive bottom
  and right offsets point inward, matching the legacy renderer. Oversized widths
  wrap within the frame; vertically clipped text is reported in the editor.
- Add/remove/reorder label rows, A-/A+ size buttons, bold/italic, expandable font,
  size, alignment, exact RGBA text colors and text-outline controls, plus a
  main-window label toggle. Clicking a rendered row selects the same inspector
  row; double-click edits it on the canvas, with Enter to apply, Shift+Enter for
  a line break and Escape to cancel. Changes synchronize in both directions.
- A collapsed Advanced label appearance section exposes exact background, border
  and shadow colors plus padding, row spacing, corner radius, border width, background
  blur, shadow blur and offsets. Each row similarly exposes exact shadow color, blur
  and offsets. Direct appearance edits automatically select Custom style.
- Opt-in pressed-key overlays in MP4: shared pixel-sized keycap rendering,
  Dark/Light/Accent/Minimal styles, draggable placement, scale and duration sliders,
  a bounded recent-shortcut stack, quick-repeat merging and linear fade-out.
  Opacity, fade duration and repeat-merge timing stay in a collapsed Advanced
  section instead of crowding the normal workflow.
  Keycap textures are prepared once and reused on the GPU; typing does not cause
  per-frame WPF rendering or screen readback. The main controller has a key toggle.
- Keyboard capture on a dedicated message-loop thread during a recording, with
  paused input discarded and the hook released on stop, cancel or failure.
  Events are filtered before entering a bounded in-memory queue; no keyboard log
  or sidecar is saved, and session data is cleared at completion. Shortcuts-only
  is the default: Ctrl/Alt/Win combinations, function keys and Shift navigation.
  Normal typing, Shift typing and conservative Right-Alt/AltGr text filtering
  protect the default mode. All Keys is unconditional once selected: ordinary
  letters, navigation, function, numpad, modifier and uncommon virtual keys are
  all rendered, including multi-modifier chords. Held-key autorepeats do not flood
  the overlay.
- Opt-in mouse-click visualization in MP4 and GIF. A low-level mouse hook runs only
  during recording; left and right clicks become colored rings at the exact captured
  pointer position. Display, region and moving-window targets map global coordinates
  into the video, and clicks outside the target or while paused are ignored. Violet,
  blue and high-contrast presets cover the common path; size, ring width, duration,
  opacity and exact left/right colors remain available. The bounded timeline is
  cleared after every session and no click history or sidecar file is retained.
- Display picker and per-display region selector with eight handles, whole-region
  dragging, display-edge snapping, size presets, aspect lock, arrow-key nudging,
  Enter/double-click confirmation, and Escape cancellation.
- Region-selection appearance is profile-specific. Violet, blue and high-contrast
  presets cover the common cases; Advanced exposes line, dash, dim, handle and
  dimension colors and sizes. The selector uses those values immediately, including
  circular or square handles and optional dimensions.
- Searchable individual-window selector with application name, title and current
  dimensions. Window identity is stored per profile and restored only when the
  title, process and window class identify exactly one open window. Minimized,
  closed or replaced windows cannot start a recording.
- Physical-pixel region geometry, a per-monitor DPI manifest, remembered regions,
  disconnected-display handling, and bounds checks after display changes. Shared
  screen-to-capture mapping covers negative virtual-desktop origins and regions on
  monitors positioned left of or above the primary display. Service windows retain
  exact physical bounds when WPF receives a DPI change.
- Persistent topmost boundary that remains visible while the application is
  running and the profile option is enabled, including while the controller is
  hidden in the notification area and throughout recording. Four narrow solid-color,
  passive, no-activate, click-through windows draw the edges without allocating a
  capture-sized layered surface. The edges use the configured recording color for
  an active session and are placed outside the crop where desktop bounds allow it.
  They do not depend on capture affinity. The native smoke and recording checks
  verify visibility, passive styles, exact placement and persistence while dynamic
  overlays update.
- Independent profile switches preview the label, pressed keys, and mouse-click
  rings over the selected capture target between recordings. Each visible label,
  keycap, or click ring owns only a small topmost, passive, click-through surface.
  These preview surfaces close before capture starts and return after it finishes.
  Every capture source composes enabled recording overlays directly on the GPU, so
  desktop-window updates cannot introduce missing, partial, or black video frames.
- Region, display and individual-window recording through Windows.Graphics.Capture, GPU cropping
  with Direct3D 11, and H.264 MP4 encoding through Windows MediaTranscoder. An event-query
  completion barrier ensures that GPU crop, overlay and resize commands finish before each
  surface is handed to the asynchronous encoder. The public recording-ready signal, active
  clock and input acceptance begin only after both the first capture frame and the encoder
  are prepared, so startup latency cannot discard early overlays or create a timestamp gap.
  Hardware acceleration is requested; actual encoder selection is system-managed.
- Individual-window capture follows the selected window when it moves and keeps
  recording it when other windows overlap it. Closing or minimizing the source,
  or changing its dimensions, stops safely and retains the unfinished MP4 with
  a specific recovery message. The target is revalidated after the countdown.
- Countdown cancellation, pause/resume without recording paused time, stop/save,
  confirmed discard, and automatic stop with convenient duration presets.
  Settings and profile switching are disabled during a recording. Closing the
  controller finishes an active recording before the application exits.
- Cursor capture, 15/30/60 and preserved custom FPS, and three bitrate presets.
  The current Auto option explicitly uses 30 FPS.
- MP4 resolution presets for Original, 960, 1280, 1920, 2560 and 3840 pixels
  wide, plus an exact profile-specific width. Aspect ratio is preserved, smaller
  captures are never enlarged, odd encoder padding stays outside the content,
  and the finished frame including labels, pressed keys and click rings is resized on the GPU.
- Hardware H.264 encoding is attempted first and preparation falls back to the
  Windows software encoder automatically. If both cannot start, a focused
  recovery window keeps the unfinished MP4 visible, can open its folder, and can
  apply Efficient/1280-wide settings for a deliberate new recording attempt.
- Dynamic pressed-key and click pixels are composited directly into the encoder
  surface through Direct2D on the recording D3D11 device. The video frame never
  takes a GPU-to-CPU staging round trip. Graphics-device creation accepts D3D
  feature levels 10.0 through 11.1 and falls back to the Windows WARP device when
  hardware device creation is unavailable.
- Collision-safe profile-specific filename templates and temporary sibling files.
  MP4 settings include opening the destination folder after save. Successful encoding is
  renamed without overwriting previous recordings. Cancel deletes only the
  session's own temporary file. Other failures retain the unfinished file and
  report its path; an incomplete MP4 is not guaranteed to be playable.
- The ten most recent MP4/GIF outputs are stored once, deduplicated without regard
  to path casing, and exposed through a compact Recent menu beside the save folder.
  The same menu opens the configured folder.
- A recording-colored boundary and elapsed/limit display while recording.
- Global recorder shortcuts using RegisterHotKey, separate from the optional
  pressed-key overlay: Ctrl+Shift+F9 starts or stops/saves, Ctrl+Shift+F8 pauses or
  resumes, and Ctrl+Shift+F10 requests discard with confirmation. Start/stop and
  discard cancel an active countdown. Holding a shortcut does not repeat actions.
  Buttons and shortcuts share the same guarded command handlers; settings dialogs,
  profile operations, region selection and shutdown cannot accidentally start a
  recording. Minimized/background recording control remains available.
- A compact Shortcuts editor: click an action and press its combination, clear
  individual assignments, or restore defaults. Assignments belong to the active
  profile, including disabled shortcuts. Legacy angle-bracket syntax is accepted.
  Duplicate/invalid combinations and OS conflicts are reported without taking over
  another application's registration. A profile with unavailable shortcuts leaves
  all global actions inactive and shows a warning; on-screen controls still work.
  Old registrations and queued actions are cleared on profile switches and exit.
- Per-profile light, dark and Windows-system themes, always-on-top behavior, and
  close-to-notification-area behavior. The tray menu can restore or exit the
  recorder; completed recordings display a clickable saved-file notification.
- Single-instance guard for the native application.
- Self-contained Windows x64 preview folder with no runtime installation needed.
- Native GIF export from the completed MP4, including the same burned-in label
  pressed-key and mouse-click overlays. Media Foundation decodes sequentially; WIC resizes,
  quantizes and writes each frame without accumulating the animation in memory.
  No Python process or FFmpeg executable is used by the native pipeline.
- A compact GIF settings dialog with size and motion presets, Match capture,
  palette and repeat choices, dithering and optional MP4 retention. Editable
  choices accept precise values; Advanced keeps frame sampling, last-frame
  duration, filename templates and opening the folder after save. Changes apply
  only on Save settings and belong to the active profile.
- Aspect-preserving GIF resizing, 2–256 palette colors, per-frame adaptive
  palettes, optional error-diffusion dithering, repeats, frame-step sampling and
  a last-frame duration override. Sample timestamps preserve recording speed;
  centisecond rounding does not accumulate fractional-frame-rate drift. Decoder
  block padding and H.264's extra odd-edge pixels are excluded from GIF content.
- Export progress with bounded UI update frequency and cancellation. Stop/pause
  shortcuts cannot start a second recording during export; Cancel stops export
  and retains the MP4. Closing during export cancels it and keeps the MP4 too.
  Existing outputs are never overwritten, and a failed/cancelled export removes
  only its own incomplete GIF. The new source MP4 is removed only after a GIF
  succeeds and only when Also keep the MP4 is off.

## Verify and run

```powershell
.\build-native.ps1
.\dist\native-preview\ScreenDemoRecorder.exe
```

Use `.\build-native.ps1 -BenchmarkStartup` for five isolated fresh-process
startup probes. The measurement runs from process creation to the first fully
rendered main window and does not reuse application state. On the current test
machine the first process took 860.15 ms and the median of the next four was
844.54 ms. Each run writes its raw samples to `build/native-startup-<id>/result.json`.

The build script runs the pure core checks and a hidden WPF smoke check using
isolated settings under `build/native-published-smoke`. It renders the main
window in MP4/GIF/window-source modes, the recent-file menu, searchable window
selector, application/selection appearance settings, MP4/GIF presets and Advanced
settings, landscape/portrait overlay editor, inline text editing,
expanded text and label-appearance controls, pressed-key settings, keycap styles,
mouse-click presets/advanced settings and region selector to PNG for layout review. Inline checks cover apply, cancel,
inspector synchronization and invalid exact colors. Label checks cover alpha,
wrapping, per-row geometry, strokes, row visibility, empty/disabled labels,
clipping, backdrop blur, container shadow and per-row shadow bounds. Timings in
the smoke report measure in-process initialization, not end-to-end cold startup.
The same smoke run asserts Per-Monitor V2 awareness, creates an excluded invisible
DPI probe on every connected display, compares WPF and Win32 DPI, and verifies
the selector's exact physical display bounds. Pure checks cover 100%, 125%, 150%
and 200% viewport scaling plus negative monitor origins even when those physical
arrangements are not connected to the build machine.
Shortcut checks exercise actual Win32 registration, conflicts (including partial
setup rollback), release/re-registration, and queued old-message removal. Internal
messages are sent only to the test's owned window; no OS keyboard input is injected.
Controller checks verify idle/disabled actions and both countdown-cancellation
commands without capturing the desktop. Preview-mode windows cannot start capture.
Core tests cover shortcut parsing, reserved keys, profile round trips, strict
profile transfer, legacy selection appearance migration, recent-file deduplication,
disabled bindings, MP4 size planning, and retaining the previous profile when
saving a new assignment fails. The WPF smoke check also exercises software-encoder
fallback selection and renders the recovery window.
GIF checks cover exact/fractional settings, validation without mutation, and
preservation of advanced values across profile duplication and reload.

For a real capture/encode/decode check, run `./build-native.ps1 -VerifyRecording`.
It briefly shows its own non-activating colored test window and captures only
that window, not the desktop. Isolated reports, MP4s and a decoded PNG go under
`build/native-recording-<id>`. It verifies GPU crop offsets, odd-size padding,
monitor interop dimensions, decoded colors before/after pause, duration excluding
pause, stop/save, paused and immediate discard, the maximum duration and
fractional FPS. It also creates a full-window capture target and verifies that a
window resize terminates recording with a clear error. A decoded scaled MP4 verifies
GPU resize dimensions and content color. A second MP4 verifies label placement, premultiplied transparency
and decoded-frame parity with a reference preview, allowing for H.264 compression.
Keyboard and mouse checks install, stop early and release real hooks with no-op consumers, then use
synthetic events directly in the recording pipeline (not OS input injection) to
verify stacked cards, default typing filters, paused-input rejection, fade-out,
expiration, exact click mapping and preview/export parity alongside a label.
Interactive keyboard/mouse delivery across applications and layouts still needs manual verification.
The same test recordings are exported to GIF and decoded again to check every sampled
background frame as well as resize,
display-aperture padding, frame counts and delays, repeats, last-frame hold,
two-color/full palettes, repeated-frame sampling, label/keys/clicks, ordered progress,
mid-export/immediate cancellation, filename collisions and unchanged source MP4s.
GIF-specific results are written to `gif-result.txt` in the same directory.
Core checks additionally cover all nine label anchors, inward margins, edge and
portrait bounds, keyboard display/privacy modes, AltGr, migrated recorder hotkey
filtering, bounded key/click histories and deterministic fade/expansion timing, filename collisions, reserved
Windows names, traversal sanitization, cancellation and unfinished-file retention.

To record: choose a region, display or individual window, choose MP4 or GIF and the output folder, then
press Record. Pause/Resume and Stop & save appear in the same compact controller.
The default MP4 keeps the source resolution; odd dimensions receive at most one
black padding pixel at the right/bottom edge for H.264 compatibility. Resolution
presets can reduce MP4 size without changing the aspect ratio. Sound and a true
FP16 HDR-to-SDR tone-mapping pipeline are not connected; Windows HDR does not need
to be disabled for capture, but HDR color accuracy is not yet guaranteed.
Labels are burned into MP4 when Show label in video is enabled. Enable Show
pressed keys for keyboard overlays; edit their appearance under Edit overlays >
Pressed keys. Keys are global across applications while recording, not limited
to the selected capture rectangle. Selecting All keys always includes ordinary
typing and can expose sensitive input; password fields are not detected. Keycaps describe
physical keys, not composed text or IME output. Right Alt is conservatively treated
as AltGr for text privacy. Recorder control shortcuts are hidden from the overlay
by default. Custom key style currently uses the dark appearance. Enable Highlight
mouse clicks to show left/right rings at the pointer; click events are global, but
only positions inside the current capture target are rendered. Supported legacy
settings are migrated while the original v1 file is retained and backed up. The
retired badge is intentionally omitted. Legacy background blur and per-row text
shadows migrate into the native label model. Label container, shadow, text-color
and stroke values are editable in the native overlay editor. The canvas uses a
neutral background rather than a live desktop preview.

For GIF, open GIF settings next to the format picker. A 960-pixel width and
12 FPS are a useful starting point; Match capture uses the selected area's width
within the supported 64–7680 pixel range. Height follows the aspect ratio and
must not exceed 7680 pixels. Above 50 effective FPS, some viewers clamp short GIF
delays and slow playback; the usual presets stay below that threshold. Repeat
counts are repeats after the first play (0 means forever). A nonzero last-frame
duration replaces that frame's usual delay. Frame sampling lowers the number of
frames without speeding up the recording. GIF creation happens after recording
stops; it can take time at large sizes. Processing memory is bounded by working
frames rather than total recording duration. GIF palette reduction and the MP4
intermediate are lossy; GIF is not intended for HDR or archival-quality video.

Keep the entire `native-preview` folder together. The EXE is not a single-file
distribution; its runtime stays beside it instead of being extracted on launch.

## Installer

```powershell
.\build-installer.ps1
.\build-installer.ps1 -VerifyPackage
```

The WiX 6 MSI is fixed-scope per-user: it installs to
`%LOCALAPPDATA%\Programs\Screen Demo Recorder` without elevation, creates one
Start Menu shortcut, registers standard uninstall and major-upgrade metadata,
and keeps its compressed CAB inside the MSI. Payload components use stable GUIDs
and HKCU registry key paths, with explicit empty-folder cleanup on uninstall.
Normal builds inspect the MSI database and verify its version, scope, embedded
CAB, upgrade rows, shortcut, 483 files, 485 components and payload byte total.
`-VerifyPackage` additionally performs a non-registering administrative extraction
under `build/native-installer-extract-<id>` and compares all payload SHA-256 hashes.
The verified 1.0.2 package is 60.91 MiB for a 197.75 MiB self-contained payload.
It is not code-signed yet.

## Next milestones

1. Manual keyboard/mouse delivery checks across normal and elevated applications,
   one real mixed-DPI display arrangement, plus encoded monitor-capture exclusion verification.
2. Automatic FP16 capture and HDR-to-SDR tone mapping verified on HDR hardware.
3. Clean-machine installer test and release signing.

The source-level functional-parity audit is complete; see
[`NATIVE_PARITY.md`](NATIVE_PARITY.md). It covers every retained Python profile
field and workflow. Python removal still waits for the manual release gates.

The native preview records silent MP4 and exports GIF with optional labels,
pressed keys and mouse-click rings. It is not a full replacement for the Python release yet.

Keyboard lifecycle follows the Windows [low-level keyboard hook guidance](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc):
the callback runs on a dedicated message-loop thread, does no rendering or file
I/O, and always passes input onward without blocking shortcuts in other apps.
Recorder control shortcuts use the separate [RegisterHotKey API](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
with MOD_NOREPEAT. Actual physical-key delivery across application focus changes,
keyboard layouts and elevated applications still needs manual verification.
Physical overlay placement follows the Windows [Per-Monitor DPI guidance](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged),
and recorder controls request the documented [`WDA_EXCLUDEFROMCAPTURE`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
mode. Capture exclusion remains mandatory for recorder controls and best-effort for
the passive boundary, whose visibility must not depend on display-affinity support.
GIF conversion follows the [Media Foundation Source Reader workflow](https://learn.microsoft.com/en-us/windows/win32/medfound/processing-media-data-with-the-source-reader)
and the Windows [WIC GIF metadata schema](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-native-image-format-metadata-queries).
