# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import tempfile
import unittest
from pathlib import Path

from screen_demo_recorder.recording import ScreenRecorder


class RecordingStateTests(unittest.TestCase):
    def test_rejects_tiny_capture_regions_before_starting_worker(self):
        recorder = ScreenRecorder()
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "at least 16"):
                recorder.start((0, 0, 10, 10), Path(directory) / "capture.mp4")
        self.assertFalse(recorder.has_session)

    def test_pause_requires_an_active_recording(self):
        with self.assertRaisesRegex(RuntimeError, "No active recording"):
            ScreenRecorder().pause()


if __name__ == "__main__":
    unittest.main()
