# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Archive a recorded video and convert it into a styled animated GIF."""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path
from typing import Callable, Iterator

from PIL import Image

from .build import build_demo, load_config


def _available_destination(directory: Path, filename: str) -> Path:
    candidate = directory / filename
    if not candidate.exists():
        return candidate
    source = Path(filename)
    number = 2
    while True:
        candidate = directory / f"{source.stem}-{number}{source.suffix}"
        if not candidate.exists():
            return candidate
        number += 1


def archive_video(source: str | Path, videos_root: Path, slug: str) -> Path:
    """Copy a user recording into the project's non-destructive media library."""

    source_path = Path(source).resolve()
    if not source_path.is_file():
        raise FileNotFoundError(source_path)
    directory = videos_root / slug
    directory.mkdir(parents=True, exist_ok=True)
    destination = _available_destination(directory, source_path.name)
    shutil.copy2(source_path, destination)
    return destination


def extract_video_frames(
    source: str | Path,
    output: Path,
    *,
    fps: float = 12.0,
    start: float = 0.0,
    end: float | None = None,
    reader_factory: Callable[..., Iterator] | None = None,
) -> int:
    """Decode a time range to numbered RGB PNG frames using FFmpeg."""

    if fps <= 0:
        raise ValueError("fps must be greater than zero")
    if start < 0:
        raise ValueError("start must not be negative")
    if end is not None and end <= start:
        raise ValueError("end must be greater than start")
    if reader_factory is None:
        from imageio_ffmpeg import read_frames

        reader_factory = read_frames
    input_params = ["-ss", str(start)] if start else []
    output_params = ["-vf", f"fps={fps:g}"]
    if end is not None:
        output_params.extend(["-t", str(end - start)])
    reader = reader_factory(
        str(Path(source).resolve()),
        pix_fmt="rgb24",
        input_params=input_params,
        output_params=output_params,
    )
    metadata = next(reader)
    width, height = metadata["size"]
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    count = 0
    try:
        for count, frame in enumerate(reader, start=1):
            image = Image.frombytes("RGB", (width, height), frame)
            image.save(output / f"{count - 1:04d}.png")
    finally:
        close = getattr(reader, "close", None)
        if close:
            close()
    if not count:
        raise RuntimeError("The selected video range produced no frames")
    return count


def video_to_gif(
    config_file: str | Path,
    source: str | Path,
    slug: str,
    *,
    title: str = "",
    subtitle: str = "",
    badge: str = "",
    fps: float = 12.0,
    start: float = 0.0,
    end: float | None = None,
) -> tuple[Path, Path, int]:
    """Archive a recording, decode it, and build its styled GIF."""

    config_path, config = load_config(config_file)
    videos_root = (config_path.parent / config.get("videos_root", "media/videos")).resolve()
    archived = archive_video(source, videos_root, slug)
    frames_root = (config_path.parent / config.get("frames_root", ".demo_frames")).resolve()
    count = extract_video_frames(
        archived,
        frames_root / slug,
        fps=fps,
        start=start,
        end=end,
    )
    config["frame_duration_ms"] = round(1000 / fps)
    config["frame_step"] = 1
    if slug not in config["demos"]:
        config["demos"][slug] = {
            "title": title or Path(source).stem.replace("-", " ").replace("_", " ").title(),
            "subtitle": subtitle,
            "badge": badge,
        }
    else:
        demo = config["demos"][slug]
        if title:
            demo["title"] = title
        if subtitle:
            demo["subtitle"] = subtitle
        if badge:
            demo["badge"] = badge
    output = build_demo(config_path, config, slug)
    return archived, output, count


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config", help="Path to the JSON project configuration")
    parser.add_argument("video", help="MP4, MOV, MKV, WebM, AVI, or other FFmpeg-readable file")
    parser.add_argument("slug", help="Stable demo name used for storage and output")
    parser.add_argument("--title", default="", help="Caption title override")
    parser.add_argument("--subtitle", default="", help="Caption subtitle override")
    parser.add_argument("--badge", default="", help="Caption badge override")
    parser.add_argument("--fps", type=float, default=12.0, help="GIF frames per second")
    parser.add_argument("--start", type=float, default=0.0, help="Start time in seconds")
    parser.add_argument("--end", type=float, help="End time in seconds")
    args = parser.parse_args()
    archived, output, count = video_to_gif(
        args.config,
        args.video,
        args.slug,
        title=args.title,
        subtitle=args.subtitle,
        badge=args.badge,
        fps=args.fps,
        start=args.start,
        end=args.end,
    )
    print(f"Archived video: {archived}")
    print(f"Decoded frames: {count}")
    print(f"GIF: {output}")


if __name__ == "__main__":
    main()

