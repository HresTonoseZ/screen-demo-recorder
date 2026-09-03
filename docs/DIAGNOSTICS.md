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
dist/screen-demo-recorder-diagnostics/ScreenDemoRecorder.exe
diagnostic-reports/<timestamp>/
    build.log                 (build console, commands, stage announcements, errors)
    summary.txt               (build stage results and overall success/failure)
    *.stdout.log              (SDK, compiler, core tests and publish output)
    *.stderr.log              (error output from those commands)
    tests/tests.log           (test starts, passes, failures and timeouts)
    tests/pipeline/           (test MP4/GIF/MKV files and detailed results)
    tests/*/diagnostics/      (diagnostic logs for each application test)
    tests/*/stdout.log        (application test console output)
    tests/*/stderr.log        (application test error output)
```

Each invocation replaces the same diagnostic build folder; no ZIP is created.
The normal build similarly uses `dist/screen-demo-recorder`. Cleanup is restricted
to these two output paths and refuses linked directories. Move manually saved
recordings, profiles or runtime logs out of an output folder before rebuilding;
everything inside that build folder is deleted. Old timestamped build folders
from previous script versions are not automatically deleted.

Reports use a new timestamped folder on every run and are never cleared by a
rebuild. Logging starts before SDK discovery, so missing-SDK and compilation
failures are included. The console announces automatic tests before they run.
Send the **entire `diagnostic-reports/<timestamp>` folder** for a failed build or
test, not the application folder. A RUNNING/START entry without a final PASS/FAIL
can indicate that the process was interrupted. Reports may contain screenshots,
test recordings and local paths; review them before sharing. Nothing is uploaded.

Test child processes and their encoders are terminated
on failure or timeout. No application is left running after successful tests.
The BAT releases its working directory before its final pause.

## Reproduce a problem on another PC

1. Copy the entire `dist/screen-demo-recorder-diagnostics` folder to a writable
   local folder on the other PC; keep all adjacent files. No installation is needed.
2. Close other copies of the recorder. Run `ScreenDemoRecorder.exe`; its main
   window title contains `[Diagnostic]`.
3. Use the same capture, export and live-presentation settings that failed.
   A fresh build may have different settings; check them before testing.
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

### Readback probe v2

Logs containing `PROBE readback-v2` separately identify texture-description
retrieval, staging-buffer creation/release, `CopySubresourceRegion`, `Map`,
mapped-row copying and `Unmap`. The first entry and exit of each operation are
flushed to disk; repeated operations remain visible in watchdog snapshots and
counters without writing per-frame entry/exit noise. Capture callback, pause
status and stop lock waits have their own markers.

This probe does not change the capture algorithm or locking strategy. For the
reported Windows 10 case, first repeat MP4 recording at 2560x1440 and 30 FPS with
live label/key/click previews disabled. If it hangs, wait 15 seconds and send
the latest run's logs. A successful run on a different PC is not a confirmed fix.

The explicit diagnostic self-test delays four individual native-call boundaries
inside the real staging readback path. It verifies the watchdog identifies each
pending stage, then releases the delay and checks the actual returned pixels.
These synthetic delays test diagnostic precision, not the remote driver's
behavior or the responsiveness of a real capture session stalled inside it.

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

After a successful build, `scripts/test-diagnostic-report.ps1 -ReportDirectory
<report-folder>` verifies that its console, stage results, per-test logs and
MP4/GIF/MKV artifacts were all retained in the report.
