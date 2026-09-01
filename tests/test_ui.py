# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PySide6.QtWidgets import QApplication

from screen_demo_recorder.settings import SettingsStore
from screen_demo_recorder.ui import GlobalHotkeys, MainWindow
from screen_demo_recorder.windows import Monitor


class UserInterfaceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.application = QApplication.instance() or QApplication([])

    def test_window_round_trips_all_bound_profile_fields(self):
        with tempfile.TemporaryDirectory() as directory:
            store = SettingsStore(Path(directory) / "settings.json")
            store.data["profiles"]["Default"]["application"]["always_on_top"] = False
            monitor = Monitor(1, 0, 0, 1920, 1080, "Test Monitor", "DISPLAY1", True)
            with (
                mock.patch("screen_demo_recorder.ui.list_monitors", return_value=[monitor]),
                mock.patch.object(GlobalHotkeys, "start"),
                mock.patch.object(GlobalHotkeys, "stop"),
            ):
                window = MainWindow(store)
                profile = window._profile_from_fields()
                self.assertGreaterEqual(len(window.fields), 90)
                self.assertEqual(profile["capture"]["mode"], "monitor")
                self.assertEqual(window._target_rectangle(profile), (0, 0, 1920, 1080))
                window._cleanup()
                window.deleteLater()


if __name__ == "__main__":
    unittest.main()
