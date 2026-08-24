# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Interactive hotkey-controlled Blender window recorder."""

from __future__ import annotations

import argparse
import tempfile
import threading
from dataclasses import dataclass
from pathlib import Path

from pynput import keyboard

from .recording import (
    BlenderWindow,
    WindowRecorder,
    enable_pixel_accurate_coordinates,
    list_blender_windows,
    validate_slug,
)
from .video import video_to_gif


@dataclass(frozen=True)
class RecordingOptions:
    window: BlenderWindow
    config: Path
    slug: str
    title: str
    subtitle: str
    badge: str
    hotkey: str
    recording_fps: float
    gif_fps: float


class RecorderController:
    """Toggle one window recording and convert the result after stop."""

    def __init__(self, options: RecordingOptions) -> None:
        self.options = options
        self.recorder = WindowRecorder()
        self.temporary: Path | None = None
        self.converting = False

    def toggle(self) -> None:
        if self.converting:
            print("GIF conversion is still running; hotkey ignored.")
            return
        if self.recorder.is_recording:
            self.stop()
        else:
            self.start()

    def start(self) -> None:
        temporary = Path(tempfile.gettempdir()) / f"blender-demo-{self.options.slug}.mp4"
        if temporary.exists():
            temporary.unlink()
        self.recorder.start(
            self.options.window.handle,
            temporary,
            self.options.recording_fps,
        )
        self.temporary = temporary
        print(f"RECORDING STARTED: {self.options.window.title}")
        print(f"Press {self.options.hotkey} again to stop.")

    def stop(self) -> None:
        self.converting = True
        temporary = None
        try:
            print("Stopping recording...")
            temporary = self.recorder.stop()
            archived, output, count = video_to_gif(
                self.options.config,
                temporary,
                self.options.slug,
                title=self.options.title,
                subtitle=self.options.subtitle,
                badge=self.options.badge,
                fps=self.options.gif_fps,
            )
            print(f"VIDEO SAVED: {archived}")
            print(f"GIF SAVED: {output}")
            print(f"GIF FRAMES: {count}")
            print(f"Press {self.options.hotkey} to record another take.")
        except Exception as error:
            print(f"RECORDING FAILED: {error}")
        finally:
            if temporary and temporary.is_file():
                temporary.unlink()
            self.temporary = None
            self.converting = False

    def cancel(self) -> None:
        self.recorder.cancel()
        if self.temporary and self.temporary.is_file():
            self.temporary.unlink()
        self.temporary = None


def _prompt(text: str, default: str = "") -> str:
    suffix = f" [{default}]" if default else ""
    value = input(f"{text}{suffix}: ").strip()
    return value or default


def choose_window(windows: list[BlenderWindow], selected: int | None = None) -> BlenderWindow:
    if not windows:
        raise RuntimeError("No visible Blender windows found")
    if selected is not None:
        if selected < 1 or selected > len(windows):
            raise ValueError(f"Window index must be between 1 and {len(windows)}")
        return windows[selected - 1]
    print("\nOpen Blender windows:")
    for index, window in enumerate(windows, start=1):
        print(f"  {index}. {window.title}")
    while True:
        try:
            index = int(_prompt("Select Blender window", "1"))
            if index < 1 or index > len(windows):
                raise ValueError
            return windows[index - 1]
        except (ValueError, IndexError):
            print(f"Enter a number from 1 to {len(windows)}.")


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--list-windows", action="store_true", help="List visible Blender windows and exit")
    parser.add_argument("--window", type=int, help="One-based Blender window index")
    parser.add_argument("--config", help="Project JSON configuration")
    parser.add_argument("--slug", help="Stable demo slug")
    parser.add_argument("--title", help="Caption title")
    parser.add_argument("--subtitle", help="Caption subtitle")
    parser.add_argument("--badge", help="Caption badge")
    parser.add_argument("--hotkey", help="pynput global hotkey")
    parser.add_argument("--recording-fps", type=float, default=30.0)
    parser.add_argument("--gif-fps", type=float, default=12.0)
    return parser.parse_args(argv)


def collect_options(args) -> RecordingOptions:
    windows = list_blender_windows()
    window = choose_window(windows, args.window)
    example = Path(__file__).resolve().parents[1] / "examples" / "demo-config.json"
    config = Path(args.config or _prompt("Project config", str(example))).resolve()
    if not config.is_file():
        raise FileNotFoundError(f"Config not found: {config}")
    slug = validate_slug(args.slug or _prompt("Demo slug", "blender-demo"))
    title = args.title if args.title is not None else _prompt("Caption title", "Blender Demo")
    subtitle = args.subtitle if args.subtitle is not None else _prompt("Caption subtitle")
    badge = args.badge if args.badge is not None else _prompt("Caption badge", "BLENDER")
    hotkey = args.hotkey or _prompt("Toggle hotkey", "<ctrl>+<shift>+<f9>")
    if args.recording_fps <= 0 or args.gif_fps <= 0:
        raise ValueError("FPS values must be greater than zero")
    return RecordingOptions(
        window,
        config,
        slug,
        title,
        subtitle,
        badge,
        hotkey,
        args.recording_fps,
        args.gif_fps,
    )


def main(argv=None) -> None:
    enable_pixel_accurate_coordinates()
    args = parse_args(argv)
    if args.list_windows:
        windows = list_blender_windows()
        if not windows:
            print("No visible Blender windows found.")
        for index, window in enumerate(windows, start=1):
            print(f"{index}: {window.label}")
        return
    try:
        options = collect_options(args)
        controller = RecorderController(options)
        listener = keyboard.GlobalHotKeys({options.hotkey: controller.toggle})
        listener.start()
    except Exception as error:
        raise SystemExit(f"Cannot start recorder: {error}") from error
    print("\nBlender Add-on Demo Recorder is running.")
    print(f"Target: {options.window.title}")
    print(f"Toggle recording: {options.hotkey}")
    print("Press Ctrl+C in this window to exit.\n")
    stopped = threading.Event()
    try:
        while not stopped.wait(1.0):
            pass
    except KeyboardInterrupt:
        print("\nClosing recorder...")
    finally:
        listener.stop()
        controller.cancel()


if __name__ == "__main__":
    main()
