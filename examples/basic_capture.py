# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Minimal Blender capture project using the default cube."""

from __future__ import annotations

import sys
from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from blender_demo_recorder import Demo, run_capture


def build_cube_move():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.mesh.primitive_cube_add()
    cube = bpy.context.object

    def update(frame):
        amount = min(max((frame - 12) / 36, 0.0), 1.0)
        cube.location.x = amount * 2.5
        cube.rotation_euler.z = amount * 1.2

    return Demo("cube-move", 64, update)


run_capture(
    {"cube-move": build_cube_move},
    ROOT / ".demo_frames",
)

