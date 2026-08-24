# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Capture deterministic Blender UI frame sequences."""

from __future__ import annotations

import shutil
import sys
import traceback
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Mapping, Sequence


@dataclass(frozen=True)
class Demo:
    """One deterministic capture sequence."""

    slug: str
    frame_count: int
    update: Callable[[int], None]


class CaptureRunner:
    """Advance demo state and save one Blender window screenshot per tick."""

    def __init__(
        self,
        builders: Sequence[Callable[[], Demo]],
        output_root: Path,
        frame_delay: float,
        startup_ticks: int,
        quit_when_done: bool,
    ) -> None:
        self.builders = builders
        self.output_root = output_root
        self.frame_delay = frame_delay
        self.startup_ticks = startup_ticks
        self.quit_when_done = quit_when_done
        self.demo_index = -1
        self.frame = 0
        self.demo: Demo | None = None
        self._startup_tick = 0

    def _dismiss_splash(self, bpy) -> None:
        window = bpy.context.window
        if not window or not hasattr(window, "event_simulate"):
            return
        try:
            window.event_simulate(type="ESC", value="PRESS")
            window.event_simulate(type="ESC", value="RELEASE")
        except RuntimeError:
            # Event simulation is optional when Blender opens a .blend file.
            pass

    def _next_demo(self) -> bool:
        self.demo_index += 1
        if self.demo_index >= len(self.builders):
            return False
        self.demo = self.builders[self.demo_index]()
        self.frame = 0
        output = self.output_root / self.demo.slug
        if output.exists():
            shutil.rmtree(output)
        output.mkdir(parents=True)
        print(f"CAPTURE_START {self.demo.slug} {self.demo.frame_count}")
        return True

    def tick(self):
        import bpy

        try:
            if self._startup_tick < self.startup_ticks:
                self._startup_tick += 1
                self._dismiss_splash(bpy)
                return 0.1
            if self.demo is None and not self._next_demo():
                print("CAPTURE_COMPLETE")
                if self.quit_when_done:
                    bpy.ops.wm.quit_blender()
                return None
            self.demo.update(self.frame)
            bpy.context.view_layer.update()
            path = self.output_root / self.demo.slug / f"{self.frame:04d}.png"
            bpy.ops.screen.screenshot(
                filepath=str(path),
                hide_props_region=True,
                check_existing=False,
            )
            self.frame += 1
            if self.frame >= self.demo.frame_count:
                print(f"CAPTURE_FINISH {self.demo.slug}")
                self.demo = None
            return self.frame_delay
        except Exception:
            traceback.print_exc()
            if self.quit_when_done:
                bpy.ops.wm.quit_blender()
            return None


def requested_slugs(argv: Sequence[str] | None = None) -> list[str]:
    """Return demo slugs supplied after Blender's ``--`` separator."""

    arguments = list(sys.argv if argv is None else argv)
    separator = arguments.index("--") if "--" in arguments else len(arguments)
    return arguments[separator + 1 :]


def run_capture(
    demo_builders: Mapping[str, Callable[[], Demo]],
    output_root: str | Path,
    *,
    slugs: Sequence[str] | None = None,
    frame_delay: float = 0.085,
    startup_ticks: int = 8,
    quit_when_done: bool = True,
) -> CaptureRunner:
    """Register a Blender timer that captures selected demos."""

    import bpy

    selected = list(slugs) if slugs is not None else requested_slugs()
    selected = selected or list(demo_builders)
    unknown = [slug for slug in selected if slug not in demo_builders]
    if unknown:
        raise ValueError(f"Unknown demo slug(s): {', '.join(unknown)}")
    root = Path(output_root).resolve()
    root.mkdir(parents=True, exist_ok=True)
    runner = CaptureRunner(
        [demo_builders[slug] for slug in selected],
        root,
        frame_delay,
        startup_ticks,
        quit_when_done,
    )
    bpy.app.timers.register(runner.tick, first_interval=1.0, persistent=False)
    return runner

