# Screen Demo Recorder UX Redesign

## Product direction

Screen Demo Recorder should feel like a small native Windows utility, not a settings form. The default surface exposes only the decisions needed before a recording. Direct manipulation replaces coordinate entry wherever the result is spatial. Profiles remain first-class and retain the complete configuration.

The C# rewrite targets .NET 10 and WPF. The Python application remains available until the native implementation reaches functional parity and passes the replacement checklist.

## Design principles

1. **Fast by default.** Common recording choices are one click away and use meaningful presets.
2. **Direct manipulation.** Regions, labels, and keystroke overlays are moved and resized on the preview itself.
3. **Progressive disclosure.** The main window stays compact. Exact numeric controls remain available in Advanced settings.
4. **Persistent spatial feedback.** A selected region remains visible before and during recording without appearing in the output.
5. **Profile fidelity.** A profile includes capture, output, overlays, selection behavior, and application behavior. Switching profiles is immediate and reversible.
6. **Safe input visualization.** Keyboard overlays show shortcuts by default, not normal typed text.

## Main recorder window

Target width is approximately 380–420 logical pixels. The window contains:

- Profile picker in the title area, with Save As, Rename, Duplicate, Import, Export, and Delete in a compact profile menu.
- Capture source segmented control: Region, Display, or Window.
- Area summary with one Select action and a Use last region shortcut.
- FPS presets: 15, 30, 60, and Auto. Custom FPS remains in Advanced.
- Output quality presets: Efficient, Balanced, and Crisp. Each preset shows the resulting format and resolution.
- Save destination summary with a folder picker. Filename templates remain in Advanced.
- Cursor, pressed-keys, and mouse-click rows with simple enable switches.
- Countdown inside the primary Record action rather than as an isolated numeric field.
- One Advanced action and one dominant Record action.

The main window does not expose X/Y coordinates, palette size, dither flags, padding values, shadow offsets, or other implementation-level values.

## Region selection

### Required interactions

- Eight resize handles: four corners and four edges.
- Drag the visible top grip or anywhere inside the selected area to move the entire rectangle.
- A visible move affordance and move cursor at the center.
- Live width × height readout.
- Arrow keys move by 1 pixel; Shift+Arrow moves by 10 pixels.
- Drag handles with Shift to preserve aspect ratio.
- Optional snapping to monitor and window edges.
- Size presets such as 640×360, 1280×720, and 1920×1080.
- Enter or double-click confirms; Escape cancels.
- The last accepted region is remembered per profile and monitor arrangement.

### Persistent recording boundary

After selection, the boundary remains visible whenever Show area boundary is enabled and the application is running, including while the controller is hidden in the notification area. It changes from the selection accent to the recording color and includes a small status tag. The passive topmost boundary never activates, receives focus, or intercepts pointer input. It requests Windows capture exclusion when available, but a display-affinity failure must never prevent the boundary from being shown. Controller capture exclusion remains mandatory.

The recording controller sits just outside the region and contains elapsed time, Pause/Resume, Stop, and Cancel. Its position can be changed if it would fall outside the monitor or cover another control.

## Visual overlay editor

Spatial overlays use one shared editor with a recording preview and a narrow property inspector. The user selects a layer, edits it on-canvas, and sees the result immediately.

### Universal label layer

- Add, remove, reorder, enable, and edit any number of text rows directly on the preview. Rows have no fixed title or subtitle role.
- Drag the complete caption to position it.
- Resize its width using handles.
- Use a 3×3 anchor grid for quick positioning.
- Choose from visual presets: Clean, Glass, Accent, and Dark.
- Use a single size slider for the common path.
- Toggle safe-area constraints and reset position.
- Keep exact font, colors, padding, border, shadow, stroke, and offsets in Advanced.

There is no badge layer and no mandatory `DEMO` capsule. Legacy title and subtitle values migrate into ordinary text rows; legacy badge values are intentionally not migrated.

### Pressed-keys layer

The common recorder window exposes only an enable switch and summary. Selecting the layer in the overlay editor exposes:

- Display mode: Shortcuts only, non-text keys, or all keys.
- Default mode: Shortcuts only.
- Position by direct drag plus the same 3×3 anchor grid.
- Appearance presets: Dark, Light, Accent, and Minimal.
- Size and opacity controls.
- Visibility duration and fade duration.
- Merge-combination window, initially 200 ms, so `Ctrl`, `Shift`, and `S` appear as one chord.
- Repeated-press treatment such as `×2` instead of rapidly duplicating the same keycap.
- Optional stack of recent shortcuts, limited to three entries.
- A Test keystroke animation action with a live preview.
- An option to hide the recorder's own start, pause, stop, and cancel shortcuts.

Printable single keys are disabled by default. Selecting All keys unconditionally enables every captured keyboard virtual key, including ordinary letters, navigation, function, numpad and standalone modifier keys. It requires an explicit privacy notice because global keyboard capture can include sensitive input. Password-field detection is not treated as a reliable safety boundary.

Keyboard events should be stored as timestamped recording metadata and composed into the final MP4 or GIF. This makes the visible result deterministic, allows filtering after capture, and avoids relying on a desktop overlay being captured correctly. Only normalized key identities and timestamps required for rendering are stored; no background input is retained outside an active recording.

### Mouse-click layer

The main recorder exposes one enable switch. The overlay editor provides left/right
color presets, ring size, width, animation duration, opacity, and test buttons. Rings
expand from the real pointer position and are burned into MP4 before GIF conversion.
Clicks outside the captured rectangle, clicks while paused, and stale session events
are discarded. Click coordinates and timestamps remain only in bounded recording memory;
no click history or sidecar file is saved.

## Advanced settings organization

Advanced settings use searchable categories without changing the compact main window:

- Capture: exact FPS, duration limit, monitor, region snapping, aspect ratio, and global shortcuts.
- Output: format, resolution, GIF palette, dithering, frame step, looping, source-video retention, and filename template.
- Overlays: universal label, pressed keys, and mouse-click visualization.
- Appearance: selection frame, recording frame, theme, always-on-top, and tray behavior.

Each category begins with a preset and then exposes exact values. Changes update the preview immediately. A Reset section action restores only that category; Reset profile remains a separate profile command.

## Profile behavior

- The active profile is always visible.
- Changes are saved automatically after a short debounce and show a subtle Saved state.
- Duplicate is the primary way to branch an existing setup.
- Import and export retain all advanced values, including overlay styles and region behavior.
- Existing Python settings are migrated once and backed up before the schema version changes.
- Region coordinates are validated against the current monitor topology. Invalid regions fall back to selection mode without discarding the rest of the profile.

## Native implementation boundaries

- WPF owns windows, profile editing, overlay editing, and the compact controller.
- Windows.Graphics.Capture owns display and window capture where supported.
- Media Foundation owns H.264 encoding.
- A low-level keyboard hook is active only during recording and is responsible for normalized, timestamped key events.
- Overlay composition uses one shared model for preview, MP4 rendering, and GIF rendering.
- Controller windows require capture exclusion. Passive region-boundary strips request it independently, but remain visible if Windows rejects display affinity.

## Phased implementation

### Phase 1 — UX contract

- Interactive recorder, region, recording-boundary, caption, and pressed-keys concepts.
- This document as the acceptance baseline.

### Phase 2 — Native shell

- .NET 10 WPF solution.
- Fast startup, single-instance behavior, tray integration, theme support, and profile migration.
- Compact main window with working profile and file settings.

### Phase 3 — Region workflow

- Native multi-monitor selector, whole-region dragging, resizing, keyboard movement, presets, snapping, and persistent passive boundary with best-effort capture exclusion.

### Phase 4 — Recording pipeline

- Display, window, and region capture.
- Pause, resume, stop, cancel, recovery, MP4 output, and GIF processing.

### Phase 5 — Overlay editor

- Universal label direct manipulation.
- Timestamped pressed-keys capture, privacy filters, preview, and composition.
- Cursor and click visualization.

### Phase 6 — Replacement

- Performance and integration tests.
- Packaging and installer.
- Functional-parity audit against the Python application.
- Remove Python only after the native release passes the audit.

## UX references

- ShareX region capture: <https://getsharex.com/docs/region-capture>
- ScreenToGif recording workflow and compact recorder: <https://github.com/NickeManarin/ScreenToGif/wiki/Help-%E2%96%AA-Recording-%F0%9F%93%B9>
- ScreenToGif interaction strings and selection-panning behavior: <https://github.com/NickeManarin/ScreenToGif/blob/master/ScreenToGif/Resources/Localization/StringResources.en.xaml>
- CleanShot recording controls, capture modes, click visualization, and keystroke options: <https://cleanshot.com/features>
- Screen Studio shortcut-overlay behavior: <https://preview.screen.studio/guide/shortcuts>
- KeyCastr input modes, positioning, and privacy considerations: <https://github.com/keycastr/keycastr>
