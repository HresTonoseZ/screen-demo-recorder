# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import tempfile
import unittest
from pathlib import Path

from PIL import Image

from blender_demo_recorder.video import _available_destination, extract_video_frames


class VideoStorageTests(unittest.TestCase):
    def test_existing_recordings_are_not_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "capture.mp4").touch()
            self.assertEqual(_available_destination(root, "capture.mp4").name, "capture-2.mp4")


class FrameExtractionTests(unittest.TestCase):
    def test_extracts_every_decoded_frame(self):
        frames = [bytes([index * 20, 30, 40] * 8) for index in range(3)]

        def reader_factory(*_args, **_kwargs):
            yield {"size": (4, 2)}
            yield from frames

        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "frames"
            count = extract_video_frames(
                "recording.mp4",
                output,
                fps=12,
                reader_factory=reader_factory,
            )
            self.assertEqual(count, 3)
            self.assertEqual(len(list(output.glob("*.png"))), 3)
            with Image.open(output / "0002.png") as image:
                self.assertEqual(image.size, (4, 2))


if __name__ == "__main__":
    unittest.main()

