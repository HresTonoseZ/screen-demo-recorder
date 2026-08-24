# Blender Add-on Demo Recorder

Reusable tooling for recording deterministic Blender UI demonstrations and
building compact, captioned animated GIFs. It is designed for repeatable add-on
marketing media rather than manual screen recording.

## Install

```powershell
python -m pip install -e .
```

This installs the `blender-demo-recorder`, `blender-demo-gif`, and
`blender-video-gif` commands. Project-specific Blender capture scripts may add
this repository directory to `sys.path`, as shown in the included example.

## Features

- Captures real Blender windows through deterministic frame callbacks.
- Records one demo or a complete named demo collection.
- Builds smooth GIFs without discarding frames by default.
- Keeps all text and visual styling in a human-readable JSON file.
- Supports global styling plus per-demo overrides.
- Uses nine caption anchors with pixel offsets.
- Controls fonts, sizes, colors, transparency, padding, border, radius, badge,
  output width, palette size, timing, looping, and frame sampling.
- Imports personally recorded MP4, MOV, MKV, WebM, AVI, and other FFmpeg-readable
  videos, archives the source, and creates a styled GIF automatically.
- Includes a Windows desktop recorder that targets one selected Blender window.
- Uses one configurable global hotkey as a start/stop recording toggle.
- Keeps raw frames separate from committed optimized media.

## Project layout

Copy `examples/basic_capture.py` and `examples/demo-config.json` into the target
project. The capture script owns project-specific Blender scenes and actions;
the recorder owns timing, screenshots, output folders, and GIF presentation.

## Capture frames

Create or open a clean `.blend` file so Blender does not cover the capture with
its startup splash. Run Blender with event simulation and the project capture
script:

```powershell
blender --enable-event-simulate clean.blend `
  --python examples/basic_capture.py -- cube-move
```

Omit demo slugs after `--` to capture every registered demo.

Each builder returns a `Demo` with a stable slug, frame count, and an `update`
callback. The callback receives the zero-based frame number and changes the
scene deterministically.

```python
from blender_demo_recorder import Demo, run_capture

def build_demo():
    def update(frame):
        object.location.x = frame / 20
    return Demo("move-object", 60, update)

run_capture({"move-object": build_demo}, ".demo_frames")
```

## Build GIFs

Install Pillow and run the builder outside Blender:

```powershell
python -m pip install -r requirements.txt
python -m blender_demo_recorder.build examples/demo-config.json
```

Pass demo slugs after the config path to build only selected GIFs.

## Convert a recorded video

Record a workflow manually with any screen recorder, then point the tool at the
file. The original video is copied into `videos_root/<slug>/`, existing files
are never overwritten, frames are extracted, and the final GIF is stored in
`output_root`. The default video library and output folder remain local and
ignored by Git so large or private recordings are not pushed accidentally.

```powershell
python -m blender_demo_recorder.video examples/demo-config.json `
  "D:\Recordings\pivot-demo.mp4" edit-pivot `
  --title "Edit Pivot" `
  --subtitle "Move and rotate with native gizmos" `
  --badge "OBJECT MODE" `
  --fps 12 `
  --start 1.5 `
  --end 8.0
```

The time range is optional. If the slug already exists in the JSON config, its
saved text and per-demo style are reused. Command-line text values override the
saved values for the current conversion.

## Blender recorder application

Launch the desktop interface:

```powershell
python -m blender_demo_recorder
```

After installation, `blender-demo-recorder` starts the same application.

The application runs in a console so it works without a GUI framework. Then:

1. Open Blender and start the recorder.
2. Select a Blender window from the numbered list.
3. Select a project JSON configuration and enter the demo text.
4. Set any global hotkey supported by `pynput`; the default is
   `Ctrl+Shift+F9`, written as `<ctrl>+<shift>+<f9>`.
5. Press the hotkey once to start recording.
6. Press the same hotkey again to stop.

The application remains active until `Ctrl+C` is pressed in its console. It
records only the selected Blender window. On stop, it archives
the MP4, extracts smooth GIF frames, applies the configured caption style, and
stores the final GIF automatically. Do not resize the selected Blender window
during an active recording; moving it is supported.

## Caption configuration

Every presentation value is editable in JSON. The default example places a
small caption at the bottom center and retains every captured frame.

Important global fields:

| Field | Purpose |
| --- | --- |
| `width` | Final GIF width in pixels |
| `frame_step` | Keep every Nth source frame; `1` keeps all frames |
| `frame_duration_ms` | Playback duration of one captured frame |
| `palette_colors` | GIF palette size from 2 to 256 |
| `loop` | `0` loops forever |

Caption `anchor` accepts `top_left`, `top_center`, `top_right`, `center_left`,
`center`, `center_right`, `bottom_left`, `bottom_center`, or `bottom_right`.
Use `offset_x` and `offset_y` for exact placement.

The `style` object controls:

- caption visibility, width, anchor, and offsets;
- horizontal and vertical padding and line spacing;
- background, border color, border width, and corner radius;
- title and subtitle font path, size, weight, and color;
- badge visibility, font, text color, background, padding, and radius.

Colors accept `#RRGGBB`, `#RRGGBBAA`, RGB arrays, or RGBA arrays. A demo can
override any global style field with its own nested `style` object.

The text is defined per demo:

```json
{
  "title": "Edit Pivot",
  "subtitle": "Move and rotate with native gizmos",
  "badge": "OBJECT MODE"
}
```

## Tests

```powershell
python -m unittest discover -s tests
```

## Project

- Version: `0.1.0`
- Maintainer: HresTonoseZ
- License: MIT
