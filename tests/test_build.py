# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from blender_demo_recorder.build import _color, _merge, build_all, load_config


class ConfigurationTests(unittest.TestCase):
    def test_nested_style_override_preserves_defaults(self):
        merged = _merge({"title": {"size": 20, "color": "white"}}, {"title": {"size": 15}})
        self.assertEqual(merged["title"], {"size": 15, "color": "white"})

    def test_hex_colors_support_alpha(self):
        self.assertEqual(_color("#11223344"), (17, 34, 51, 68))
        self.assertEqual(_color("#112233"), (17, 34, 51, 255))

    def test_config_requires_demos(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "config.json"
            path.write_text("{}", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "At least one demo"):
                load_config(path)


class BuildTests(unittest.TestCase):
    def test_builds_smooth_captioned_gif(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            frames = root / "frames" / "sample"
            frames.mkdir(parents=True)
            for index in range(6):
                image = Image.new("RGB", (320, 180), (20 + index * 15, 30, 50))
                image.save(frames / f"{index:04d}.png")
            config = {
                "frames_root": "frames",
                "output_root": "output",
                "width": 240,
                "frame_step": 1,
                "frame_duration_ms": 85,
                "demos": {
                    "sample": {
                        "title": "Edit Pivot",
                        "subtitle": "Native gizmo workflow",
                        "badge": "OBJECT MODE"
                    }
                }
            }
            config_path = root / "config.json"
            config_path.write_text(json.dumps(config), encoding="utf-8")
            output = build_all(config_path)[0]
            with Image.open(output) as result:
                self.assertEqual(result.size, (240, 135))
                self.assertEqual(result.n_frames, 6)
                self.assertEqual(result.info["duration"], 80)


if __name__ == "__main__":
    unittest.main()

