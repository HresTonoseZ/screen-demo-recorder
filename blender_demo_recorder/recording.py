# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Windows Blender-window discovery and MP4 screen recording."""

from __future__ import annotations

import ctypes
import ctypes.wintypes
import re
import sys
import threading
import time
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class BlenderWindow:
    handle: int
    title: str

    @property
    def label(self) -> str:
        return f"{self.title}  [window {self.handle}]"


def validate_slug(value: str) -> str:
    slug = value.strip().lower()
    if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", slug):
        raise ValueError("Slug must use lowercase letters, numbers, and single hyphens")
    return slug


def enable_pixel_accurate_coordinates() -> None:
    if sys.platform != "win32":
        return
    try:
        ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    except (AttributeError, OSError):
        ctypes.windll.user32.SetProcessDPIAware()


def list_blender_windows() -> list[BlenderWindow]:
    if sys.platform != "win32":
        return []
    user32 = ctypes.windll.user32
    windows: list[BlenderWindow] = []
    callback_type = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)

    def visit(handle, _extra):
        if not user32.IsWindowVisible(handle):
            return True
        length = user32.GetWindowTextLengthW(handle)
        if not length:
            return True
        buffer = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(handle, buffer, length + 1)
        title = buffer.value.strip()
        if "Blender" in title:
            windows.append(BlenderWindow(int(handle), title))
        return True

    user32.EnumWindows(callback_type(visit), 0)
    return sorted(windows, key=lambda item: item.title.casefold())


def window_capture_rect(handle: int) -> tuple[int, int, int, int]:
    if sys.platform != "win32":
        raise RuntimeError("Window recording is currently supported on Windows only")
    user32 = ctypes.windll.user32
    if not user32.IsWindow(handle):
        raise RuntimeError("The selected Blender window no longer exists")
    if user32.IsIconic(handle):
        raise RuntimeError("Restore the selected Blender window before recording")
    rect = ctypes.wintypes.RECT()
    if not user32.GetWindowRect(handle, ctypes.byref(rect)):
        raise ctypes.WinError()
    width = rect.right - rect.left
    height = rect.bottom - rect.top
    width -= width % 2
    height -= height % 2
    if width < 2 or height < 2:
        raise RuntimeError("The selected Blender window has no recordable area")
    return rect.left, rect.top, width, height


class WindowRecorder:
    """Record one selected Blender window to H.264 MP4 on a worker thread."""

    def __init__(self) -> None:
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self._error: BaseException | None = None
        self.output: Path | None = None

    @property
    def is_recording(self) -> bool:
        return bool(self._thread and self._thread.is_alive())

    def start(self, handle: int, output: str | Path, fps: float = 30.0) -> None:
        if self.is_recording:
            raise RuntimeError("A recording is already active")
        if fps <= 0:
            raise ValueError("Recording FPS must be greater than zero")
        self.output = Path(output).resolve()
        self.output.parent.mkdir(parents=True, exist_ok=True)
        self._stop.clear()
        self._error = None
        self._thread = threading.Thread(
            target=self._record,
            args=(handle, self.output, fps),
            name="BlenderWindowRecorder",
            daemon=True,
        )
        self._thread.start()

    def stop(self) -> Path:
        if not self._thread:
            raise RuntimeError("No recording is active")
        self._stop.set()
        self._thread.join()
        self._thread = None
        if self._error:
            error = self._error
            self._error = None
            raise RuntimeError(f"Recording failed: {error}") from error
        if not self.output or not self.output.is_file():
            raise RuntimeError("Recording did not produce a video file")
        return self.output

    def cancel(self) -> None:
        """Stop without requiring a completed output file."""

        if not self._thread:
            return
        self._stop.set()
        self._thread.join()
        self._thread = None

    def _record(self, handle: int, output: Path, fps: float) -> None:
        try:
            import mss
            from imageio_ffmpeg import write_frames

            left, top, width, height = window_capture_rect(handle)
            writer = write_frames(
                str(output),
                (width, height),
                fps=fps,
                codec="libx264",
                quality=7,
                macro_block_size=2,
                ffmpeg_log_level="error",
                output_params=["-movflags", "+faststart"],
            )
            writer.send(None)
            interval = 1.0 / fps
            deadline = time.perf_counter()
            try:
                with mss.mss() as screen:
                    while not self._stop.is_set():
                        current_left, current_top, current_width, current_height = window_capture_rect(handle)
                        if (current_width, current_height) != (width, height):
                            raise RuntimeError("Do not resize the Blender window while recording")
                        shot = screen.grab(
                            {
                                "left": current_left,
                                "top": current_top,
                                "width": width,
                                "height": height,
                            }
                        )
                        writer.send(shot.rgb)
                        deadline += interval
                        self._stop.wait(max(0.0, deadline - time.perf_counter()))
            finally:
                writer.close()
        except BaseException as error:
            self._error = error
