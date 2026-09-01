# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Safe video-to-GIF post-processing pipeline."""

from __future__ import annotations

import os
import re
import shutil
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Callable, Iterator
from uuid import uuid4

from PIL import Image

from .render import apply_caption


@dataclass(frozen=True)
class ProcessingResult:
    gif: Path
    source_video: Path | None
    frame_count: int
    width: int
    height: int


def available_path(directory: Path, filename: str) -> Path:
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


def _filename_part(value: str, fallback: str = "recording") -> str:
    clean = re.sub(r"[^A-Za-z0-9._ -]+", "", value).strip(" ._-")
    clean = re.sub(r"\s+", "-", clean)
    return clean or fallback


def output_path(profile: dict[str, Any], recorded_at: datetime | None = None) -> Path:
    output = profile["output"]
    caption = profile["caption"]
    moment = recorded_at or datetime.now()
    directory = Path(output["directory"]).expanduser().resolve()
    directory.mkdir(parents=True, exist_ok=True)
    template = str(output["filename_template"])
    title = _filename_part(str(caption.get("title", {}).get("text", "")))
    values = {
        "date": moment.strftime("%Y-%m-%d"),
        "time": moment.strftime("%H-%M-%S"),
        "title": title,
        "counter": "1",
    }
    try:
        stem = _filename_part(template.format_map(values))
    except KeyError as error:
        raise ValueError(f"Unknown filename placeholder: {error.args[0]}") from error
    candidate = directory / f"{stem}.gif"
    if not candidate.exists():
        return candidate
    counter = 2
    while True:
        values["counter"] = str(counter)
        numbered = _filename_part(template.format_map(values))
        if numbered == stem:
            numbered = f"{stem}-{counter}"
        candidate = directory / f"{numbered}.gif"
        if not candidate.exists():
            return candidate
        counter += 1


def decode_video_frames(
    source: str | Path,
    *,
    fps: float,
    reader_factory: Callable[..., Iterator] | None = None,
) -> tuple[tuple[int, int], Iterator[bytes]]:
    if fps <= 0:
        raise ValueError("GIF FPS must be greater than zero")
    if reader_factory is None:
        from imageio_ffmpeg import read_frames

        reader_factory = read_frames
    reader = reader_factory(
        str(Path(source).resolve()),
        pix_fmt="rgb24",
        output_params=["-vf", f"fps={fps:g}"],
    )
    metadata = next(reader)
    width, height = metadata["size"]
    return (int(width), int(height)), reader


def _build_gif(
    source: Path,
    destination: Path,
    profile: dict[str, Any],
    *,
    reader_factory: Callable[..., Iterator] | None = None,
) -> tuple[int, int, int]:
    output = profile["output"]
    source_size, reader = decode_video_frames(source, fps=float(profile["capture"]["gif_fps"]), reader_factory=reader_factory)
    source_width, source_height = source_size
    target_width = int(output["width"])
    target_height = max(1, round(source_height * target_width / source_width))
    step = int(output["frame_step"])
    frames: list[Image.Image] = []
    try:
        for index, raw in enumerate(reader):
            if index % step:
                continue
            image = Image.frombytes("RGB", source_size, raw)
            if image.size != (target_width, target_height):
                image = image.resize((target_width, target_height), Image.Resampling.LANCZOS)
            frames.append(apply_caption(image, profile["caption"]))
    finally:
        close = getattr(reader, "close", None)
        if close is not None:
            close()
    if not frames:
        raise RuntimeError("The recording produced no decodable GIF frames")

    colors = int(output["palette_colors"])
    palette_source = frames[0].quantize(colors=colors, method=Image.Quantize.MEDIANCUT)
    palette = palette_source.getpalette()
    dither = Image.Dither.FLOYDSTEINBERG if output["dither"] else Image.Dither.NONE
    quantized: list[Image.Image] = []
    for frame in frames:
        paletted = frame.quantize(palette=palette_source, dither=dither)
        paletted.putpalette(palette)
        quantized.append(paletted)

    duration = max(1, round(1000 / float(profile["capture"]["gif_fps"])) * step)
    durations = [duration] * len(quantized)
    final_duration = int(output.get("final_frame_duration_ms", 0))
    if final_duration:
        durations[-1] = final_duration
    temporary = destination.with_name(f".{destination.stem}.{uuid4().hex}.part.gif")
    try:
        quantized[0].save(
            temporary,
            save_all=True,
            append_images=quantized[1:],
            duration=durations,
            loop=int(output["loop"]),
            optimize=True,
            disposal=2,
        )
        os.rename(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)
    return len(quantized), target_width, target_height


def process_recording(
    source: str | Path,
    profile: dict[str, Any],
    *,
    recorded_at: datetime | None = None,
    reader_factory: Callable[..., Iterator] | None = None,
) -> ProcessingResult:
    """Create a non-overwriting GIF and optionally retain the source MP4."""

    source_path = Path(source).resolve()
    if not source_path.is_file():
        raise FileNotFoundError(source_path)
    destination = output_path(profile, recorded_at)
    archived: Path | None = None
    if profile["output"].get("save_source_video"):
        video_directory = destination.parent / "Source Videos"
        video_directory.mkdir(parents=True, exist_ok=True)
        archived = available_path(video_directory, destination.with_suffix(".mp4").name)
        shutil.copy2(source_path, archived)
    count, width, height = _build_gif(source_path, destination, profile, reader_factory=reader_factory)
    return ProcessingResult(destination, archived, count, width, height)
