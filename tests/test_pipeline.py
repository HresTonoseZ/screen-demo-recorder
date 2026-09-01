# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import tempfile
import unittest
from datetime import datetime
from pathlib import Path

from PIL import Image

from screen_demo_recorder.pipeline import process_recording
from screen_demo_recorder.settings import default_profile


class PipelineTests(unittest.TestCase):
    def test_builds_non_overwriting_captioned_gifs(self):
        width, height = 80, 50

        def reader_factory(*_args, **_kwargs):
            yield {"size": (width, height)}
            for index in range(4):
                yield bytes([20 + index * 25, 30, 50] * width * height)

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "recording.mp4"
            source.touch()
            profile = default_profile()
            profile["output"]["directory"] = str(root / "output")
            profile["output"]["filename_template"] = "sample"
            profile["output"]["width"] = 160
            profile["caption"]["width"] = 140
            profile["capture"]["gif_fps"] = 10
            first = process_recording(source, profile, recorded_at=datetime(2026, 1, 2, 3, 4, 5), reader_factory=reader_factory)
            second = process_recording(source, profile, recorded_at=datetime(2026, 1, 2, 3, 4, 5), reader_factory=reader_factory)
            self.assertEqual(first.gif.name, "sample.gif")
            self.assertEqual(second.gif.name, "sample-2.gif")
            self.assertEqual(first.frame_count, 4)
            self.assertEqual((first.width, first.height), (160, 100))
            with Image.open(first.gif) as result:
                self.assertEqual(result.size, (160, 100))
                self.assertEqual(result.n_frames, 4)
                self.assertEqual(result.info["duration"], 100)

    def test_retains_source_video_when_requested(self):
        def reader_factory(*_args, **_kwargs):
            yield {"size": (16, 16)}
            yield bytes([10, 20, 30] * 16 * 16)

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "capture.mp4"
            source.write_bytes(b"video")
            profile = default_profile()
            profile["output"]["directory"] = str(root / "output")
            profile["output"]["width"] = 64
            profile["caption"]["width"] = 64
            profile["output"]["save_source_video"] = True
            result = process_recording(source, profile, reader_factory=reader_factory)
            self.assertIsNotNone(result.source_video)
            self.assertEqual(result.source_video.read_bytes(), b"video")


if __name__ == "__main__":
    unittest.main()
