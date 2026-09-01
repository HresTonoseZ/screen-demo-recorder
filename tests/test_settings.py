# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import json
import tempfile
import unittest
from pathlib import Path

from screen_demo_recorder.settings import SettingsStore, default_profile, merge_settings, validate_profile


class SettingsTests(unittest.TestCase):
    def test_nested_merge_preserves_sibling_values(self):
        merged = merge_settings({"title": {"size": 20, "color": "white"}}, {"title": {"size": 15}})
        self.assertEqual(merged["title"], {"size": 15, "color": "white"})

    def test_store_round_trips_named_profiles(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "settings.json"
            store = SettingsStore(path)
            profile = default_profile()
            profile["caption"]["title"]["text"] = "Second Profile"
            store.save_as("Second", profile)
            loaded = SettingsStore(path)
            self.assertEqual(loaded.active_name, "Second")
            self.assertEqual(loaded.active_profile["caption"]["title"]["text"], "Second Profile")

    def test_invalid_import_does_not_mutate_profiles(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            store = SettingsStore(root / "settings.json")
            imported = root / "broken.json"
            imported.write_text(json.dumps({"schema_version": 1, "name": "Broken", "profile": {"capture": {"mode": "window"}}}), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "capture.mode"):
                store.import_profile(imported)
            self.assertEqual(store.profile_names, ["Default"])

    def test_region_must_have_positive_dimensions(self):
        profile = default_profile()
        profile["capture"]["region"] = [10, 10, 2, 2]
        with self.assertRaisesRegex(ValueError, "too small"):
            validate_profile(profile)

    def test_unknown_fields_and_invalid_colors_are_rejected(self):
        profile = default_profile()
        profile["capture"]["unexpected"] = True
        with self.assertRaisesRegex(ValueError, "capture.unexpected"):
            validate_profile(profile)
        profile = default_profile()
        profile["caption"]["background"] = "blue"
        with self.assertRaisesRegex(ValueError, "caption.background"):
            validate_profile(profile)


if __name__ == "__main__":
    unittest.main()
