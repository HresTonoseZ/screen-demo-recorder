# Native functional-parity audit

## Conclusion

The C# implementation covers every retained user-facing workflow and profile
setting from the Python recorder. The native version also adds window capture,
direct region manipulation, MP4 output, universal label rows, pressed-key and
mouse-click composition, recording recovery, and a current-user MSI.

The legacy badge is the only intentionally retired profile feature. This follows
the approved UX contract: title and subtitle become ordinary label rows, while
badge settings are ignored. Sound and HDR are not counted as parity gaps because
neither recorder connects them to its recording pipeline.

Source-level functional parity is complete. Replacing the Python release remains
blocked by the manual operating-system checks listed below, not by missing profile
settings or recording commands.

## Workflow matrix

| Python workflow | Native result | Evidence |
| --- | --- | --- |
| Display and region capture | Complete and expanded with individual-window capture | Core coordinate checks, WPF selector smoke check, real recording smoke check |
| Manual region coordinates | Replaced with whole-region dragging, eight resize handles, keyboard movement, presets, snapping, aspect lock, and exact advanced values | Region geometry checks and selector render |
| Recording FPS, countdown, and duration | Complete; common presets stay in the main window and exact values are in **Advanced capture** | Capture settings smoke check and profile round trip |
| Cursor capture | Complete | Main-window profile persistence and recording pipeline |
| Start/stop, pause/resume, and discard hotkeys | Complete with direct key assignment, conflict detection, disabled bindings, and countdown routing | Core hotkey checks and real Win32 registration smoke check |
| GIF width, FPS, palette, dither, repeats, sampling, final hold, and source retention | Complete | Core planning plus encoded/decoded GIF smoke checks |
| Output folder, filename template, collision safety, open-after-save, and recent files | Complete and shared by MP4/GIF | Output, profile-transfer, and WPF menu checks |
| Fixed title/subtitle caption | Expanded to any number of directly editable label rows | Profile migration, label geometry, editor, preview, and encoded-frame checks |
| Font file path | Complete; native rendering resolves `.ttf`/`.otf` metadata as a WPF font family | Label renderer smoke check |
| Caption colors, padding, border, blur, container shadow, text stroke, and per-row shadow | Complete | Renderer pixels, bounds, profile transfer, UI validation, and encoded-frame parity |
| Badge / `DEMO` capsule | Intentionally retired | Migration and schema checks assert that no badge property survives |
| Region-line, handle, dimming, and dimension appearance | Complete and expanded with a separate recording color and persistent passive boundary | Profile migration, selection settings smoke check, passive-style and physical-placement checks |
| Profiles: switch, duplicate, rename, delete, reset, import, and export | Complete with atomic writes and strict schema validation | Dependency-free profile-store checks |
| Theme, always-on-top, tray behavior, and close lifecycle | Complete | WPF theme, notification-area, close, and profile checks |
| Failed/cancelled recording and GIF processing | Expanded with recoverable partial MP4 handling, progress, and cancellation | Recording-output, recovery-window, real recording, and GIF smoke checks |

## Automated contract

`ParityContractChecks` exercises the complete retained v1 profile surface with
non-default values. It verifies capture geometry and timing, output behavior,
all label-container and text-row appearance values, selection appearance,
application behavior, recent recordings, canonical legacy hotkeys, opt-in defaults
for new input overlays, and absence of the retired badge.

Run the audit and UI checks with:

```powershell
.\build-native.ps1
```

Run the real encode/decode checks with:

```powershell
.\build-native.ps1 -VerifyRecording
```

Run the installer database and byte-for-byte extraction checks with:

```powershell
.\build-installer.ps1 -VerifyPackage
```

## Remaining manual release gates

- Confirm physical keyboard and mouse delivery while normal and elevated target
  applications are focused, including at least one non-English keyboard layout.
- Confirm selection, boundary, controller, and overlay placement on a real
  mixed-DPI multi-monitor arrangement.
- Record a full display and confirm the controller is excluded while the boundary
  remains visible for the complete session. A full-display boundary is drawn just
  inside desktop edges and is therefore expected in that recording.
- Install, update, launch, and uninstall the MSI on a clean Windows user profile.
- Sign the release executable and MSI before public distribution.

Python removal should happen only after these gates pass.
