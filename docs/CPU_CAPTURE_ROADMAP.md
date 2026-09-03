# CPU Capture and Offline Overlay Roadmap

## Goal

Replace live GPU overlay composition with a deterministic two-stage pipeline:

1. Record a clean screen stream and timestamped input events.
2. Render labels, keystrokes, and click effects into the saved video after capture.

The recorder must still show the complete live presentation layer on the desktop:
the capture boundary, labels, pressed keys, and mouse-click effects. Those windows
must never appear in the clean recording.

## Non-negotiable invariants

- Windows Graphics Capture is used only to obtain the source frame. WGC exposes a
  Direct3D surface, but the frame is copied to system memory immediately.
- No Direct2D or Direct3D overlay drawing is allowed in the recording path.
- The clean intermediate contains no application-owned boundary or overlay pixels.
- Live and exported overlays consume the same pause-aware event timeline and the
  same immutable profile snapshot.
- A failed final render preserves the clean intermediate and event timeline.
- MP4 and GIF are derived from the same fully composed video, so their visible
  content cannot diverge.
- Capture, event collection, CPU rendering, and encoding have separate ownership
  and cancellation lifecycles.

## Target architecture

```text
                         +-----------------------------+
Keyboard/mouse hooks --->| pause-aware event timeline  |----+
                         +-----------------------------+    |
                                                            v
WGC frame --> staging texture --> CPU BGRA --> clean lossless intermediate
                                                            |
Live event fan-out --> excluded WPF overlay windows          |
                                                            v
                                      CPU frame renderer + timeline
                                                            |
                                                            v
                                               final composed MP4
                                                            |
                                              +-------------+-------------+
                                              |                           |
                                              v                           v
                                           keep MP4                  export GIF
```

## Technical decisions

### 1. Clean CPU recording path

- Keep WGC for display, region, and window selection.
- Copy each selected frame to a reusable `D3D11_USAGE_STAGING` texture.
- Wait for the copy, map the texture with `D3D11_MAP_READ`, and copy rows into a
  pooled BGRA buffer while respecting the mapped row pitch.
- Send CPU frames through a bounded channel to a dedicated encoder worker. The
  capture callback must never encode or block on disk I/O.
- Keep constant frame rate by sampling the latest complete frame on the recording
  clock. Reuse the previous complete frame when WGC has not supplied a newer one.
- Encode a temporary lossless or mathematically reversible intermediate with a
  CPU codec. The preferred first implementation is FFV1 in Matroska through a
  bundled LGPL-compatible FFmpeg build. HuffYUV is the fallback if compatibility
  testing exposes an FFV1 problem.
- Do not use hardware H.264 for the clean intermediate. Hardware encoders remain
  optional only for the final delivery encode after correctness is proven.

### 2. Recording session and timeline

Create one temporary session directory containing:

```text
session.json
clean.mkv
events.jsonl
rendering.partial.mp4
```

`session.json` stores the schema version, source geometry, frame rate, recording
clock origin, active duration, profile snapshot, and application version.

`events.jsonl` stores append-only pause-aware events:

- keystroke chord and privacy-filtered display text;
- mouse button, capture-relative pixel position, and timestamp;
- pause and resume boundaries;
- optional diagnostic markers such as dropped or repeated source frames.

All timestamps use the same monotonic recording clock. Paused time is excluded at
the source instead of corrected separately by each consumer.

### 3. Live presentation layer

- Keep `DesktopOverlayWindow` and `RegionBoundary` visible during countdown and
  recording.
- Require `WDA_EXCLUDEFROMCAPTURE` for every live overlay and boundary window.
- Keep physical placement best-effort during Per-Monitor V2 DPI transitions; do
  not fail recording because WPF adjusted a preview window by a pixel.
- Verify capture exclusion after creating every window.
- If exclusion cannot be enabled or verified, do not start an unsafe live layer.
  Disable it for that session, show a clear warning, and continue with clean
  capture only. Never silently risk baking the live overlay into the source.
- Fan each accepted event to two consumers: the live overlay and the timeline
  writer. Neither consumer owns the global hooks.
- Closing, cancelling, or failing a session must dispose all overlay windows and
  hooks deterministically.

### 4. Offline CPU compositor

- Decode `clean.mkv` to BGRA frames with FFmpeg.
- Pre-render label, keycap, and click assets once into premultiplied BGRA byte
  arrays using the existing renderers.
- Composite those arrays into decoded frames with a managed software alpha
  blender. It must support clipping, opacity, and bilinear resizing for animated
  click rings.
- Implement label background blur as a bounded separable CPU blur over the label
  container. It must read from an unmodified copy of the clean frame.
- Use the existing timeline/layout services to select visible events and compute
  positions for every output timestamp.
- Pipe completed BGRA frames to FFmpeg for the final H.264 MP4 encode.
- Generate GIF only from the completed composed MP4. This makes MP4 and GIF share
  exactly the same overlay frames and timing.
- Write to a partial file and atomically rename only after FFmpeg exits cleanly and
  the result passes basic decode validation.

### 5. FFmpeg distribution and licensing

- Bundle one pinned Windows x64 FFmpeg build; do not depend on `PATH`.
- Use an LGPL-compatible build unless a future license decision explicitly allows
  GPL components.
- Record the version, download URL, checksum, configuration, and license text.
- Add a third-party notices file before packaging the binary.
- Prefer original implementation over copied source. When MIT-licensed code is
  copied, retain its copyright and permission notice as required by the license.
- Architectural references:
  - OpenScreen: <https://github.com/getopenscreen/openscreen> (MIT)
  - FollowCursor: <https://github.com/sabbour/followcursor> (MIT)
  - ScreenToGif: <https://github.com/NickeManarin/ScreenToGif> (MS-PL; reference
    only unless its file-level license obligations are reviewed first)

## Current implementation status

- Session manifests, the recoverable JSONL journal, and the pause-aware clock are implemented.
- Clean WGC staging readback and the bundled CPU FFV1 intermediate encoder are implemented and tested with real frames.
- Capture-excluded live boundary, label, keystroke, and click windows are implemented and covered by WGC pixel probes.
- The offline path now decodes FFV1 to BGRA, composites existing label/key/click rasters with a managed premultiplied-alpha blender, applies bounded separable label blur, and creates H.264 MP4 with the LGPL OpenH264 CPU encoder.
- Normal MP4/GIF recording now uses one CPU session owner for clean capture, the recoverable journal, live event fan-out, offline composition, output resizing, and final OpenH264 encoding.
- Frame-accurate render progress, cancellation that retains the clean session, and startup recovery of MP4/GIF output are implemented.
- The legacy GPU recording compositor, scaler, Media Transcoding path, and architecture-specific tests have been removed.
- The cross-computer endurance matrix remains pending.

## Delivery phases

### Phase 1: Session model and event ownership

- Add versioned session and event models.
- Give keyboard and mouse hooks one owner and fan events out to live/timeline
  consumers.
- Add pause-aware timestamp tests, JSON round-trip tests, and crash-safe append
  tests.

Exit criterion: a synthetic recording produces a valid session manifest and a
deterministic event timeline without changing the current video path.

### Phase 2: Clean CPU intermediate recorder

- Implement staging-texture readback and pooled frame buffers.
- Add the bounded encoder channel and CPU FFV1 writer.
- Record clean display, region, and window intermediates.
- Add repeated-frame and backpressure diagnostics.

Exit criterion: ten-minute clean recordings contain no black/transparent frames,
keep constant timing, and decode on a second Windows computer.

### Phase 3: Safe live overlays

- Restore boundary, label, keystroke, and click windows during recording.
- Make capture exclusion mandatory and verified.
- Drive live overlays from the shared event source.
- Add automated capture probes proving that visible overlay windows are absent
  from the clean intermediate.

Exit criterion: the user sees every enabled live element while pixel checks prove
that none of those elements exists in captured frames.

### Phase 4: Offline CPU compositor

- Add FFmpeg decode and encode process wrappers with cancellation and diagnostics.
- Add software alpha blending, click scaling, and label blur.
- Compose label-only, keys-only, clicks-only, and combined sessions.
- Preserve the clean intermediate on any failure.

Exit criterion: reference frames match the existing visual renderers within the
defined pixel tolerance, with zero GPU compositor calls.

### Phase 5: Product integration

- Change the stop flow to `Finalizing recording` followed by `Rendering overlays`.
- Expose progress and cancellation without deleting the recoverable clean source.
- Apply existing output sizing and quality settings to the final encode.
- Route GIF generation through the completed composed MP4.
- Add recovery UI for unfinished sessions found on the next launch.

Exit criterion: normal MP4/GIF workflows require no manual intermediate-file
management and cancellation is recoverable.

### Phase 6: Remove the old path

Status: complete. The published verification now exercises the CPU path for both
MP4 and GIF, including capture-excluded live overlays.

- Delete the Direct2D recording compositor and GPU scaler after parity passes.
- Remove obsolete GPU synchronization code and tests that assert the retired
  architecture.
- Update user and technical documentation.

Exit criterion: repository search finds no Direct2D drawing into recording frames,
and the only Direct3D recording use is WGC acquisition and staging readback.

## Verification matrix

Run every scenario for MP4 and GIF:

| Source | Live layer | Exported layer |
| --- | --- | --- |
| Display | none | none |
| Display | label | label |
| Display | keys | keys |
| Display | clicks | clicks |
| Display | all | all |
| Region | all | all |
| Window | all | all |

For each scenario:

- decode every frame, not selected thumbnails;
- reject black, transparent, duplicated-corrupt, or discontinuous frames;
- verify the clean intermediate contains no overlay pixels;
- verify active overlay intervals contain the expected pixels continuously;
- verify pause/resume removes paused time from both video and events;
- exercise 30 and 60 FPS, odd crop dimensions, output resizing, negative monitor
  origins, and mixed-DPI displays;
- repeat rapid clicks and key bursts for at least ten minutes;
- repeat with software final H.264 encoding and every supported hardware encoder;
- run the portable build on at least two computers with different GPU vendors.

## Completion definition

The migration is complete only when:

- live overlays remain visible and are proven absent from the clean capture;
- the final MP4 and GIF contain identical intended overlay timing;
- no recording-frame code uses Direct2D or Direct3D composition;
- a render failure leaves a usable clean recording;
- automated full-frame checks pass repeatedly on the development machine;
- the previously affected computer completes the full verification matrix without
  visible flicker.
