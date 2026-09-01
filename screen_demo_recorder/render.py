# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Shared caption renderer for live preview and final GIF frames."""

from __future__ import annotations

from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFilter, ImageFont


ANCHORS = {
    "top_left", "top_center", "top_right",
    "center_left", "center", "center_right",
    "bottom_left", "bottom_center", "bottom_right",
}


def color(value: str | list[int]) -> tuple[int, int, int, int]:
    if isinstance(value, list):
        if len(value) == 3:
            return int(value[0]), int(value[1]), int(value[2]), 255
        if len(value) == 4:
            return tuple(int(channel) for channel in value)
        raise ValueError("Color arrays require three or four channels")
    text = str(value).strip().removeprefix("#")
    if len(text) == 6:
        text += "FF"
    if len(text) != 8:
        raise ValueError(f"Invalid color: {value}")
    try:
        return tuple(int(text[index:index + 2], 16) for index in range(0, 8, 2))
    except ValueError as error:
        raise ValueError(f"Invalid color: {value}") from error


def _font(style: dict[str, Any]) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    size = max(6, int(style.get("size", 14)))
    requested = Path(str(style.get("font", "")))
    if requested.is_file():
        return ImageFont.truetype(str(requested), size)
    bold = bool(style.get("bold"))
    italic = bool(style.get("italic"))
    if bold and italic:
        names = ("segoeuiz.ttf", "arialbi.ttf")
    elif bold:
        names = ("segoeuib.ttf", "arialbd.ttf")
    elif italic:
        names = ("segoeuii.ttf", "ariali.ttf")
    else:
        names = ("segoeui.ttf", "arial.ttf")
    fonts = Path("C:/Windows/Fonts")
    for name in names:
        candidate = fonts / name
        if candidate.is_file():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default()


def _wrap(draw: ImageDraw.ImageDraw, text: str, font: Any, width: int) -> str:
    lines: list[str] = []
    for paragraph in str(text).splitlines() or [""]:
        words = paragraph.split()
        if not words:
            lines.append("")
            continue
        current = words[0]
        for word in words[1:]:
            candidate = f"{current} {word}"
            if draw.textlength(candidate, font=font) <= width:
                current = candidate
            else:
                lines.append(current)
                current = word
        lines.append(current)
    return "\n".join(lines)


def _text_box(draw: ImageDraw.ImageDraw, text: str, font: Any, spacing: int) -> tuple[int, int]:
    if not text:
        return 0, 0
    box = draw.multiline_textbbox((0, 0), text, font=font, spacing=spacing)
    return box[2] - box[0], box[3] - box[1]


def _position(image_size: tuple[int, int], box_size: tuple[int, int], style: dict[str, Any]) -> tuple[int, int]:
    anchor = str(style.get("anchor", "bottom_center"))
    if anchor not in ANCHORS:
        raise ValueError(f"Unknown caption anchor: {anchor}")
    if anchor == "center":
        vertical = horizontal = "center"
    else:
        vertical, horizontal = anchor.split("_")
    image_width, image_height = image_size
    box_width, box_height = box_size
    offset_x = int(style.get("offset_x", 0))
    offset_y = int(style.get("offset_y", 0))
    x = {
        "left": offset_x,
        "center": (image_width - box_width) // 2 + offset_x,
        "right": image_width - box_width - offset_x,
    }[horizontal]
    y = {
        "top": offset_y,
        "center": (image_height - box_height) // 2 + offset_y,
        "bottom": image_height - box_height - offset_y,
    }[vertical]
    return x, y


def _aligned_x(left: int, width: int, text_width: int, alignment: str) -> int:
    if alignment == "left":
        return left
    if alignment == "right":
        return left + width - text_width
    return left + (width - text_width) // 2


def _draw_text(
    layer: Image.Image,
    position: tuple[int, int],
    text: str,
    font: Any,
    style: dict[str, Any],
    *,
    spacing: int,
) -> None:
    if not text:
        return
    shadow_color = color(style.get("shadow_color", "#00000000"))
    shadow_blur = max(0, int(style.get("shadow_blur", 0)))
    shadow_x = int(style.get("shadow_offset_x", 0))
    shadow_y = int(style.get("shadow_offset_y", 0))
    stroke_width = max(0, int(style.get("stroke_width", 0)))
    stroke_fill = color(style.get("stroke_color", "#000000FF"))
    if shadow_color[3] and (shadow_blur or shadow_x or shadow_y):
        shadow = Image.new("RGBA", layer.size, (0, 0, 0, 0))
        ImageDraw.Draw(shadow).multiline_text(
            (position[0] + shadow_x, position[1] + shadow_y),
            text,
            font=font,
            fill=shadow_color,
            spacing=spacing,
            stroke_width=stroke_width,
            stroke_fill=stroke_fill,
        )
        if shadow_blur:
            shadow = shadow.filter(ImageFilter.GaussianBlur(shadow_blur))
        layer.alpha_composite(shadow)
    ImageDraw.Draw(layer).multiline_text(
        position,
        text,
        font=font,
        fill=color(style.get("color", "#FFFFFFFF")),
        spacing=spacing,
        stroke_width=stroke_width,
        stroke_fill=stroke_fill,
    )


def render_caption_overlay(size: tuple[int, int], caption: dict[str, Any]) -> Image.Image:
    """Render the complete caption onto a transparent image of ``size``."""

    overlay = Image.new("RGBA", size, (0, 0, 0, 0))
    if not caption.get("enabled", True):
        return overlay
    measure = ImageDraw.Draw(overlay)
    padding_x = max(0, int(caption.get("padding_x", 20)))
    padding_y = max(0, int(caption.get("padding_y", 14)))
    line_gap = max(0, int(caption.get("line_gap", 5)))
    width = min(max(80, int(caption.get("width", 560))), size[0])
    content_width = max(1, width - padding_x * 2)
    title_style = caption.get("title", {})
    subtitle_style = caption.get("subtitle", {})
    title_font = _font(title_style)
    subtitle_font = _font(subtitle_style)
    title = _wrap(measure, title_style.get("text", ""), title_font, content_width) if title_style.get("enabled", True) else ""
    subtitle = _wrap(measure, subtitle_style.get("text", ""), subtitle_font, content_width) if subtitle_style.get("enabled", True) else ""
    title_size = _text_box(measure, title, title_font, line_gap)
    subtitle_size = _text_box(measure, subtitle, subtitle_font, line_gap)
    blocks = [height for text, (_, height) in ((title, title_size), (subtitle, subtitle_size)) if text]
    height = padding_y * 2 + sum(blocks) + (line_gap if len(blocks) > 1 else 0)
    x, y = _position(size, (width, height), caption)
    radius = max(0, int(caption.get("corner_radius", 12)))

    shadow_color = color(caption.get("shadow_color", "#00000000"))
    shadow_blur = max(0, int(caption.get("shadow_blur", 0)))
    if shadow_color[3]:
        shadow = Image.new("RGBA", size, (0, 0, 0, 0))
        sx = x + int(caption.get("shadow_offset_x", 0))
        sy = y + int(caption.get("shadow_offset_y", 0))
        ImageDraw.Draw(shadow).rounded_rectangle((sx, sy, sx + width, sy + height), radius=radius, fill=shadow_color)
        if shadow_blur:
            shadow = shadow.filter(ImageFilter.GaussianBlur(shadow_blur))
        overlay.alpha_composite(shadow)

    draw = ImageDraw.Draw(overlay)
    draw.rounded_rectangle(
        (x, y, x + width, y + height),
        radius=radius,
        fill=color(caption.get("background", "#00000000")),
        outline=color(caption.get("border", "#00000000")),
        width=max(0, int(caption.get("border_width", 0))),
    )
    alignment = str(caption.get("text_alignment", "center"))
    text_y = y + padding_y
    if title:
        title_x = _aligned_x(x + padding_x, content_width, title_size[0], alignment)
        _draw_text(overlay, (title_x, text_y), title, title_font, title_style, spacing=line_gap)
        text_y += title_size[1] + (line_gap if subtitle else 0)
    if subtitle:
        subtitle_x = _aligned_x(x + padding_x, content_width, subtitle_size[0], alignment)
        _draw_text(overlay, (subtitle_x, text_y), subtitle, subtitle_font, subtitle_style, spacing=line_gap)

    badge = caption.get("badge", {})
    badge_text = str(badge.get("text", "")) if badge.get("enabled", True) else ""
    if badge_text:
        badge_font = _font(badge)
        text_width, text_height = _text_box(draw, badge_text, badge_font, 0)
        badge_width = int(badge.get("width", 0)) or text_width + int(badge.get("padding_x", 10)) * 2
        badge_height = int(badge.get("height", 0)) or text_height + int(badge.get("padding_y", 4)) * 2
        position = str(badge.get("position", "top_right"))
        vertical, horizontal = position.split("_")
        badge_x = {
            "left": x + padding_x,
            "center": x + (width - badge_width) // 2,
            "right": x + width - badge_width - padding_x,
        }[horizontal] + int(badge.get("offset_x", 0))
        badge_y = (
            y - badge_height // 2 if vertical == "top" else y + padding_y
        ) + int(badge.get("offset_y", 0))
        badge_shadow_color = color(badge.get("shadow_color", "#00000000"))
        if badge_shadow_color[3]:
            badge_shadow = Image.new("RGBA", size, (0, 0, 0, 0))
            shadow_x = badge_x + int(badge.get("shadow_offset_x", 0))
            shadow_y = badge_y + int(badge.get("shadow_offset_y", 0))
            ImageDraw.Draw(badge_shadow).rounded_rectangle(
                (shadow_x, shadow_y, shadow_x + badge_width, shadow_y + badge_height),
                radius=max(0, int(badge.get("corner_radius", 7))),
                fill=badge_shadow_color,
            )
            badge_shadow_blur = max(0, int(badge.get("shadow_blur", 0)))
            if badge_shadow_blur:
                badge_shadow = badge_shadow.filter(ImageFilter.GaussianBlur(badge_shadow_blur))
            overlay.alpha_composite(badge_shadow)
            draw = ImageDraw.Draw(overlay)
        draw.rounded_rectangle(
            (badge_x, badge_y, badge_x + badge_width, badge_y + badge_height),
            radius=max(0, int(badge.get("corner_radius", 7))),
            fill=color(badge.get("background", "#00000000")),
            outline=color(badge.get("border", "#00000000")),
            width=max(0, int(badge.get("border_width", 0))),
        )
        badge_text_x = badge_x + (badge_width - text_width) // 2
        badge_text_y = badge_y + (badge_height - text_height) // 2
        _draw_text(overlay, (badge_text_x, badge_text_y), badge_text, badge_font, badge, spacing=0)
    return overlay


def apply_caption(image: Image.Image, caption: dict[str, Any]) -> Image.Image:
    """Return an RGB frame with the configured caption composited."""

    base = image.convert("RGBA")
    overlay = render_caption_overlay(base.size, caption)
    blur = max(0, int(caption.get("background_blur", 0)))
    if blur:
        blurred = base.filter(ImageFilter.GaussianBlur(blur))
        base = Image.composite(blurred, base, overlay.getchannel("A"))
    base.alpha_composite(overlay)
    return base.convert("RGB")
