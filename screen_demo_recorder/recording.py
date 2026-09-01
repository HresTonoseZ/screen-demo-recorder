# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Threaded monitor and region recording for Windows."""

from __future__ import annotations

import threading
import time
from pathlib import Path


class ScreenRecorder:
    """Record one physical desktop rectangle to H.264 MP4."""

    def __init__(self) -> None:
        self._stop = threading.Event()
        self._pause = threading.Event()
        self._thread: threading.Thread | None = None
        self._error: BaseException | None = None
        self._output: Path | None = None
        self._frames_written = 0
        self._started_at = 0.0
        self._active_seconds = 0.0
        self._limit_reached = False

    @property
    def is_recording(self) -> bool:
        return self._thread is not None and self._thread.is_alive()

    @property
    def is_paused(self) -> bool:
        return self._pause.is_set()

    @property
    def has_session(self) -> bool:
        return self._thread is not None

    @property
    def frames_written(self) -> int:
        return self._frames_written

    @property
    def active_seconds(self) -> float:
        if not self.is_recording or self.is_paused:
            return self._active_seconds
        return self._active_seconds + max(0.0, time.perf_counter() - self._started_at)

    @property
    def limit_reached(self) -> bool:
        return self._limit_reached

    def start(
        self,
        rectangle: tuple[int, int, int, int],
        output: str | Path,
        *,
        fps: float = 30.0,
        capture_cursor: bool = True,
        maximum_duration_seconds: float = 0.0,
    ) -> None:
        if self._thread is not None:
            raise RuntimeError("A recording session already exists")
        if fps <= 0:
            raise ValueError("Recording FPS must be greater than zero")
        left, top, width, height = (int(item) for item in rectangle)
        width -= width % 2
        height -= height % 2
        if width < 16 or height < 16:
            raise ValueError("The capture area must be at least 16 × 16 pixels")
        self._output = Path(output).resolve()
        self._output.parent.mkdir(parents=True, exist_ok=True)
        self._output.unlink(missing_ok=True)
        self._stop.clear()
        self._pause.clear()
        self._error = None
        self._frames_written = 0
        self._active_seconds = 0.0
        self._limit_reached = False
        self._started_at = time.perf_counter()
        self._thread = threading.Thread(
            target=self._record,
            args=((left, top, width, height), self._output, float(fps), bool(capture_cursor), float(maximum_duration_seconds)),
            name="ScreenRecorder",
            daemon=True,
        )
        self._thread.start()

    def pause(self) -> None:
        if not self.is_recording:
            raise RuntimeError("No active recording can be paused")
        if self._pause.is_set():
            return
        self._active_seconds += max(0.0, time.perf_counter() - self._started_at)
        self._pause.set()

    def resume(self) -> None:
        if not self.is_recording:
            raise RuntimeError("No active recording can be resumed")
        if not self._pause.is_set():
            return
        self._started_at = time.perf_counter()
        self._pause.clear()

    def toggle_pause(self) -> bool:
        if self.is_paused:
            self.resume()
        else:
            self.pause()
        return self.is_paused

    def stop(self) -> Path:
        if self._thread is None:
            raise RuntimeError("No recording session exists")
        if self.is_recording and not self.is_paused:
            self._active_seconds += max(0.0, time.perf_counter() - self._started_at)
        self._stop.set()
        self._thread.join()
        self._thread = None
        self._pause.clear()
        if self._error is not None:
            error = self._error
            self._error = None
            raise RuntimeError(f"Recording failed: {error}") from error
        if self._frames_written == 0 or self._output is None or not self._output.is_file():
            raise RuntimeError("Recording did not produce a video file")
        return self._output

    def cancel(self) -> None:
        if self._thread is not None:
            self._stop.set()
            self._thread.join()
            self._thread = None
        self._pause.clear()
        if self._output is not None:
            self._output.unlink(missing_ok=True)
        self._error = None
        self._frames_written = 0

    def _record(
        self,
        rectangle: tuple[int, int, int, int],
        output: Path,
        fps: float,
        capture_cursor: bool,
        maximum_duration_seconds: float,
    ) -> None:
        writer = None
        cursor_painter = None
        try:
            import mss
            from imageio_ffmpeg import write_frames

            from .cursor import CursorPainter

            left, top, width, height = rectangle
            writer = write_frames(
                str(output),
                (width, height),
                fps=fps,
                codec="libx264",
                quality=7,
                macro_block_size=2,
                ffmpeg_log_level="error",
                output_params=["-pix_fmt", "yuv420p", "-movflags", "+faststart"],
            )
            writer.send(None)
            interval = 1.0 / fps
            deadline = time.perf_counter()
            active_started = deadline
            with mss.MSS() as capture:
                if capture_cursor:
                    cursor_painter = CursorPainter(width, height, left, top)
                monitor = {"left": left, "top": top, "width": width, "height": height}
                while not self._stop.is_set():
                    if self._pause.is_set():
                        self._stop.wait(0.03)
                        deadline = time.perf_counter()
                        active_started = deadline
                        continue
                    now = time.perf_counter()
                    elapsed = self._active_seconds + (now - active_started)
                    if maximum_duration_seconds > 0 and elapsed >= maximum_duration_seconds:
                        self._active_seconds = elapsed
                        self._limit_reached = True
                        break
                    frame = capture.grab(monitor)
                    if frame.width != width or frame.height != height:
                        raise RuntimeError("The selected display area changed during recording")
                    writer.send(cursor_painter.composite(frame.bgra) if cursor_painter else frame.rgb)
                    self._frames_written += 1
                    deadline += interval
                    wait = deadline - time.perf_counter()
                    if wait > 0:
                        self._stop.wait(wait)
                    else:
                        deadline = time.perf_counter()
        except BaseException as error:
            self._error = error
        finally:
            if cursor_painter is not None:
                cursor_painter.close()
            if writer is not None:
                try:
                    writer.close()
                except BaseException as error:
                    if self._error is None:
                        self._error = error
