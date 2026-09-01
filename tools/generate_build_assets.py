# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Generate the Windows icon and version resource used by PyInstaller."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from screen_demo_recorder import __version__


def _version_tuple() -> tuple[int, int, int, int]:
    values = [int(part) for part in __version__.split(".")]
    return tuple((values + [0, 0, 0, 0])[:4])


def generate_icon(path: Path) -> None:
    scale = 4
    image = Image.new("RGBA", (256 * scale, 256 * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((12 * scale, 12 * scale, 244 * scale, 244 * scale), radius=52 * scale, fill=(15, 23, 36, 255))
    draw.rounded_rectangle(
        (27 * scale, 27 * scale, 229 * scale, 229 * scale),
        radius=40 * scale,
        outline=(76, 151, 255, 255),
        width=10 * scale,
    )
    draw.ellipse((78 * scale, 78 * scale, 178 * scale, 178 * scale), fill=(220, 62, 82, 255))
    corner = (238, 244, 255, 255)
    width = 10 * scale
    segments = [
        ((49, 88), (49, 49), (88, 49)),
        ((168, 49), (207, 49), (207, 88)),
        ((207, 168), (207, 207), (168, 207)),
        ((88, 207), (49, 207), (49, 168)),
    ]
    for first, second, third in segments:
        draw.line(tuple(coordinate * scale for point in (first, second, third) for coordinate in point), fill=corner, width=width, joint="curve")
    image = image.resize((256, 256), Image.Resampling.LANCZOS)
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])


def generate_version_file(path: Path) -> None:
    version = _version_tuple()
    text = f"""VSVersionInfo(
  ffi=FixedFileInfo(
    filevers={version},
    prodvers={version},
    mask=0x3f,
    flags=0x0,
    OS=0x40004,
    fileType=0x1,
    subtype=0x0,
    date=(0, 0)
  ),
  kids=[
    StringFileInfo([
      StringTable(
        '040904B0',
        [StringStruct('CompanyName', 'HresTonoseZ'),
         StringStruct('FileDescription', 'Screen Demo Recorder'),
         StringStruct('FileVersion', '{__version__}'),
         StringStruct('InternalName', 'ScreenDemoRecorder'),
         StringStruct('LegalCopyright', 'Copyright 2026 HresTonoseZ'),
         StringStruct('OriginalFilename', 'ScreenDemoRecorder.exe'),
         StringStruct('ProductName', 'Screen Demo Recorder'),
         StringStruct('ProductVersion', '{__version__}')])
    ]),
    VarFileInfo([VarStruct('Translation', [1033, 1200])])
  ]
)\n"""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    generate_icon(args.output / "ScreenDemoRecorder.ico")
    generate_version_file(args.output / "version-info.txt")


if __name__ == "__main__":
    main()
