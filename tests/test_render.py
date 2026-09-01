# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import unittest

from PIL import Image

from screen_demo_recorder.render import apply_caption, color, render_caption_overlay
from screen_demo_recorder.settings import default_profile


class RenderTests(unittest.TestCase):
    def test_hex_colors_support_alpha(self):
        self.assertEqual(color("#11223344"), (17, 34, 51, 68))
        self.assertEqual(color("#112233"), (17, 34, 51, 255))

    def test_preview_overlay_contains_caption_pixels(self):
        caption = default_profile()["caption"]
        overlay = render_caption_overlay((640, 360), caption)
        self.assertEqual(overlay.size, (640, 360))
        self.assertIsNotNone(overlay.getbbox())

    def test_final_renderer_preserves_frame_dimensions(self):
        frame = Image.new("RGB", (320, 180), (20, 30, 40))
        rendered = apply_caption(frame, default_profile()["caption"])
        self.assertEqual(rendered.mode, "RGB")
        self.assertEqual(rendered.size, frame.size)
        self.assertNotEqual(rendered.getpixel((160, 160)), frame.getpixel((160, 160)))


if __name__ == "__main__":
    unittest.main()
