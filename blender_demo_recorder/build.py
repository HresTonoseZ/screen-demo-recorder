# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Build configurable, captioned animated GIFs from captured PNG frames."""

from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


DEFAULT_STYLE = {
    "anchor": "bottom_center",
    "offset_x": 0,
    "offset_y": 18,
    "width": 520,
    "padding_x": 18,
    "padding_y": 11,
    "line_gap": 4,
    "corner_radius": 10,
    "background": "#090E18D9",
    "border": "#3E89FFA0",
    "border_width": 1,
    "title": {"size": 20, "color": "#FFFFFFFF", "font": "", "bold": True},
    "subtitle": {"size": 13, "color": "#C4D0E4FF", "font": "", "bold": False},
    "badge": {
        "enabled": True,
        "size": 11,
        "color": "#FFFFFFFF",
        "background": "#2F70EEEB",
        "font": "",
        "bold": True,
        "padding_x": 10,
        "padding_y": 4,
        "corner_radius": 6,
    },
}

ANCHORS = {
    "top_left",
    "top_center",
    "top_right",
    "center_left",
    "center",
    "center_right",
    "bottom_left",
    "bottom_center",
    "bottom_right",
}


def _merge(base: dict, override: dict) -> dict:
    result = copy.deepcopy(base)
    for key, value in override.items():
        if isinstance(value, dict) and isinstance(result.get(key), dict):
            result[key] = _merge(result[key], value)
        else:
            result[key] = value
    return result


def _color(value: str | list[int]) -> tuple[int, int, int, int]:
    if isinstance(value, list):
        if len(value) == 3:
            return (*value, 255)
        if len(value) == 4:
            return tuple(value)
        raise ValueError("Color arrays require three or four channels")
    text = value.removeprefix("#")
    if len(text) == 6:
        text += "FF"
    if len(text) != 8:
        raise ValueError(f"Invalid color: {value}")
    return tuple(int(text[index : index + 2], 16) for index in range(0, 8, 2))


def _font(config: dict):
    path = Path(config.get("font", ""))
    if path.is_file():
        return ImageFont.truetype(str(path), int(config["size"]))
    windows = Path("C:/Windows/Fonts")
    names = ("segoeuib.ttf", "arialbd.ttf") if config.get("bold") else ("segoeui.ttf", "arial.ttf")
    for name in names:
        candidate = windows / name
        if candidate.is_file():
            return ImageFont.truetype(str(candidate), int(config["size"]))
    return ImageFont.load_default()


def _text_size(draw, text, font):
    box = draw.textbbox((0, 0), text, font=font)
    return box[2] - box[0], box[3] - box[1]


def _position(image_size, box_size, style):
    anchor = style["anchor"]
    if anchor not in ANCHORS:
        raise ValueError(f"Unknown caption anchor: {anchor}")
    if anchor == "center":
        vertical = horizontal = "center"
    else:
        vertical, horizontal = anchor.split("_")
    image_width, image_height = image_size
    box_width, box_height = box_size
    offset_x = int(style["offset_x"])
    offset_y = int(style["offset_y"])
    x = {"left": offset_x, "center": (image_width - box_width) // 2 + offset_x, "right": image_width - box_width - offset_x}[horizontal]
    y = {"top": offset_y, "center": (image_height - box_height) // 2 + offset_y, "bottom": image_height - box_height - offset_y}[vertical]
    return x, y


def draw_caption(image: Image.Image, demo: dict, style: dict) -> None:
    if not style.get("enabled", True):
        return
    draw = ImageDraw.Draw(image, "RGBA")
    title_font = _font(style["title"])
    subtitle_font = _font(style["subtitle"])
    badge_style = style["badge"]
    badge_font = _font(badge_style)
    width = min(int(style["width"]), image.width)
    padding_x = int(style["padding_x"])
    padding_y = int(style["padding_y"])
    line_gap = int(style["line_gap"])
    title = demo.get("title", "")
    subtitle = demo.get("subtitle", "")
    badge = demo.get("badge", "")
    _, title_height = _text_size(draw, title, title_font)
    _, subtitle_height = _text_size(draw, subtitle, subtitle_font)
    box_height = padding_y * 2 + title_height + (line_gap + subtitle_height if subtitle else 0)
    x, y = _position(image.size, (width, box_height), style)
    draw.rounded_rectangle(
        (x, y, x + width, y + box_height),
        radius=int(style["corner_radius"]),
        fill=_color(style["background"]),
        outline=_color(style["border"]),
        width=int(style["border_width"]),
    )
    title_width, _ = _text_size(draw, title, title_font)
    draw.text((x + (width - title_width) / 2, y + padding_y), title, font=title_font, fill=_color(style["title"]["color"]))
    if subtitle:
        subtitle_width, _ = _text_size(draw, subtitle, subtitle_font)
        draw.text((x + (width - subtitle_width) / 2, y + padding_y + title_height + line_gap), subtitle, font=subtitle_font, fill=_color(style["subtitle"]["color"]))
    if badge and badge_style.get("enabled", True):
        badge_width, badge_height = _text_size(draw, badge, badge_font)
        badge_width += int(badge_style["padding_x"]) * 2
        badge_height += int(badge_style["padding_y"]) * 2
        badge_x = x + width - badge_width - padding_x
        badge_y = y - badge_height // 2
        draw.rounded_rectangle(
            (badge_x, badge_y, badge_x + badge_width, badge_y + badge_height),
            radius=int(badge_style["corner_radius"]),
            fill=_color(badge_style["background"]),
        )
        text_width, text_height = _text_size(draw, badge, badge_font)
        draw.text(
            (badge_x + (badge_width - text_width) / 2, badge_y + (badge_height - text_height) / 2),
            badge,
            font=badge_font,
            fill=_color(badge_style["color"]),
        )


def load_config(path: str | Path) -> tuple[Path, dict]:
    config_path = Path(path).resolve()
    config = json.loads(config_path.read_text(encoding="utf-8"))
    config["style"] = _merge(DEFAULT_STYLE, config.get("style", {}))
    if not config.get("demos"):
        raise ValueError("At least one demo is required")
    return config_path, config


def build_demo(config_path: Path, config: dict, slug: str) -> Path:
    demo = config["demos"][slug]
    frames_root = (config_path.parent / config.get("frames_root", ".demo_frames")).resolve()
    output_root = (config_path.parent / config.get("output_root", "dist")).resolve()
    paths = sorted((frames_root / slug).glob("*.png"))
    if not paths:
        raise RuntimeError(f"No captured frames for {slug}")
    width = int(config.get("width", 800))
    frame_step = int(config.get("frame_step", 1))
    if frame_step < 1:
        raise ValueError("frame_step must be at least 1")
    style = _merge(config["style"], demo.get("style", {}))
    frames = []
    for index, path in enumerate(paths):
        if index % frame_step:
            continue
        with Image.open(path) as source:
            image = source.convert("RGB")
        height = round(image.height * width / image.width)
        image = image.resize((width, height), Image.Resampling.LANCZOS)
        draw_caption(image, demo, style)
        frames.append(image)
    colors = int(config.get("palette_colors", 96))
    palette_source = frames[0].quantize(colors=colors, method=Image.Quantize.MEDIANCUT)
    palette = palette_source.getpalette()
    quantized = []
    for frame in frames:
        paletted = frame.quantize(palette=palette_source, dither=Image.Dither.FLOYDSTEINBERG)
        paletted.putpalette(palette)
        quantized.append(paletted)
    output_root.mkdir(parents=True, exist_ok=True)
    output = output_root / f"{slug}.gif"
    quantized[0].save(
        output,
        save_all=True,
        append_images=quantized[1:],
        duration=int(config.get("frame_duration_ms", 85)) * frame_step,
        loop=int(config.get("loop", 0)),
        optimize=True,
        disposal=2,
    )
    return output


def build_all(config_file: str | Path, slugs: list[str] | None = None) -> list[Path]:
    config_path, config = load_config(config_file)
    selected = slugs or list(config["demos"])
    unknown = [slug for slug in selected if slug not in config["demos"]]
    if unknown:
        raise ValueError(f"Unknown demo slug(s): {', '.join(unknown)}")
    return [build_demo(config_path, config, slug) for slug in selected]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config", help="Path to the JSON project configuration")
    parser.add_argument("slugs", nargs="*", help="Optional demo slugs")
    args = parser.parse_args()
    for output in build_all(args.config, args.slugs):
        print(f"{output.name}: {output.stat().st_size / 1024 / 1024:.2f} MiB")


if __name__ == "__main__":
    main()
