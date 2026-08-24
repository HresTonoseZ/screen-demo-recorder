# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import unittest

from blender_demo_recorder.app import choose_window, parse_args
from blender_demo_recorder.recording import BlenderWindow, validate_slug


class SlugTests(unittest.TestCase):
    def test_valid_slug_is_normalized(self):
        self.assertEqual(validate_slug("  Edit-Pivot  "), "edit-pivot")

    def test_spaces_and_underscores_are_rejected(self):
        for value in ("edit pivot", "edit_pivot", "-edit", "edit-"):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    validate_slug(value)


class ApplicationTests(unittest.TestCase):
    def test_selects_requested_blender_window(self):
        windows = [BlenderWindow(10, "First - Blender"), BlenderWindow(20, "Second - Blender")]
        self.assertEqual(choose_window(windows, 2), windows[1])

    def test_hotkey_is_interactively_configurable_by_default(self):
        self.assertIsNone(parse_args([]).hotkey)


if __name__ == "__main__":
    unittest.main()
