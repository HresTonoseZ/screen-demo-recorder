# Native diagnostic builds

Double-click `build-diagnostic.bat`, or run `./build-diagnostic.ps1` from
PowerShell. It builds the **local source** without fetching or updating Git.
The same .NET SDK 10.0.400 as the normal build is required; `-DotNet` accepts
an explicit SDK executable. If missing, `build-native.bat` can install the SDK
with your permission. NuGet restore may access the network when packages are
not already cached; FFmpeg comes only from the vendored local runtime.

The script builds a self-contained Windows x64 application, runs the core,
UI, real MP4/GIF recording and diagnostic checks, then creates:

```text
dist/native-diagnostic-<timestamp>/ScreenDemoRecorder.exe
dist/native-diagnostic-<timestamp>.zip
build/native-diagnostic-<timestamp>/   (test results, not packaged)
```

Each invocation uses a new output folder and never overwrites an older build,
profiles or recordings. Test child processes and their encoders are terminated
on failure or timeout. No application is left running after successful tests.
The BAT releases its working directory before its final pause.

## Reproduce a problem on another PC

1. Extract the entire ZIP into a writable local folder; keep all adjacent files.
2. Close other copies of the recorder. Run `ScreenDemoRecorder.exe`; its main
   window title contains `[Diagnostic]`.
3. Use the same capture, export and live-presentation settings that failed.
   A newly extracted copy may have different settings; check them before testing.
4. If the window hangs, wait at least 15 seconds before ending the application
   in Task Manager. Avoid restarting the computer before collecting the logs.
5. Send the files for the latest run from `diagnostics` beside the EXE. If the
   application folder is not writable, use
   `%LOCALAPPDATA%/Screen Demo Recorder/Diagnostics` instead.

The log includes build revision, OS/runtime/CPU/display-driver metadata,
recording settings without label text, capture/render/GIF stages, elapsed
operation times, frame counts, managed thread IDs/apartments, UI responsiveness,
CPU/memory use and exception stacks/HRESULTs. Each entry is flushed immediately.
No screen pixels, typed keys, label text or click positions are logged. Error
messages may include local paths and the Windows account name. The diagnostic
logger does not upload anything or modify registry settings.

Logs rotate at 8 MiB, keeping at most five parts per run. Startup and rotation
remove owned logs older than seven days and retain at most 20 owned log files
per folder. Active or inaccessible logs are not forcibly removed; those can
temporarily exceed the retention limit. Unrelated filenames are never pruned.
Copy the relevant logs before running many more tests. Logs and binaries remain
ignored by Git.

The watchdog observes; it does not fix, interrupt or restart stalled recording.
It cannot continue if Windows itself freezes, and the log is not a complete
native thread dump. A second diagnostic pass may still be necessary.

## Build separation and automated checks

`RecorderDiagnostics=true` defines `RECORDER_DIAGNOSTICS`. Conditional call
sites and `Diagnostics/*.cs` are compiled only in that variant. Normal builds
contain no diagnostic logger, watchdog, exception subscriptions or deliberate
stall tests. Diagnostic `obj/diagnostic` and `bin/diagnostic` caches are isolated
from normal builds, so switching variants cannot reuse an instrumented binary.

`scripts/test-native-build.ps1 -Executable <exe> -TestDirectory <new-folder>`
checks the normal flavor, UI, live overlays and real MP4/GIF output. Add
`-Diagnostic` to also verify rotation, retention, exception logging, a deliberate
UI stall and log persistence after the parent kills the test process.

The deliberate stalls are entered only with explicit test commands
`--diagnostic-log-self-test <output-folder>` or
`--diagnostic-force-stop-test <output-folder>`. They never run during normal
interactive recording. Verification on a development PC does not establish
that a failure on another Windows installation has been fixed.
