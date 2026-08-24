# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import unittest

from blender_demo_recorder.recording import validate_slug


class SlugTests(unittest.TestCase):
    def test_valid_slug_is_normalized(self):
        self.assertEqual(validate_slug("  Edit-Pivot  "), "edit-pivot")

    def test_spaces_and_underscores_are_rejected(self):
        for value in ("edit pivot", "edit_pivot", "-edit", "edit-"):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    validate_slug(value)


if __name__ == "__main__":
    unittest.main()

