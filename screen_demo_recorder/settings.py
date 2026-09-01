# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Persistent, validated application profiles."""

from __future__ import annotations

import copy
import json
import os
from pathlib import Path
from typing import Any


SCHEMA_VERSION = 1
DEFAULT_PROFILE_NAME = "Default"


def _default_output_directory() -> str:
    pictures = Path.home() / "Pictures"
    return str(pictures / "Screen Demo Recorder")


DEFAULT_PROFILE: dict[str, Any] = {
    "capture": {
        "mode": "monitor",
        "monitor": 1,
        "region": None,
        "region_lock_aspect": False,
        "region_aspect_width": 16,
        "region_aspect_height": 9,
        "region_snap_to_edges": True,
        "region_minimum_size": 32,
        "recording_fps": 30.0,
        "gif_fps": 12.0,
        "capture_cursor": True,
        "countdown_seconds": 3,
        "maximum_duration_seconds": 60,
        "toggle_hotkey": "<ctrl>+<shift>+<f9>",
        "pause_hotkey": "<ctrl>+<shift>+<f8>",
        "cancel_hotkey": "<ctrl>+<shift>+<f10>",
    },
    "output": {
        "directory": _default_output_directory(),
        "filename_template": "{date}_{time}_{title}_{counter}",
        "width": 960,
        "palette_colors": 128,
        "dither": True,
        "loop": 0,
        "frame_step": 1,
        "final_frame_duration_ms": 0,
        "save_source_video": False,
        "open_folder_after_save": False,
    },
    "caption": {
        "enabled": True,
        "anchor": "bottom_center",
        "offset_x": 0,
        "offset_y": 24,
        "width": 560,
        "padding_x": 20,
        "padding_y": 14,
        "line_gap": 5,
        "text_alignment": "center",
        "corner_radius": 12,
        "background": "#090E18D9",
        "background_blur": 0,
        "border": "#3E89FFA0",
        "border_width": 1,
        "shadow_color": "#00000070",
        "shadow_blur": 8,
        "shadow_offset_x": 0,
        "shadow_offset_y": 4,
        "title": {
            "enabled": True,
            "text": "Screen Demo",
            "size": 22,
            "color": "#FFFFFFFF",
            "font": "",
            "bold": True,
            "italic": False,
            "stroke_width": 0,
            "stroke_color": "#000000FF",
            "shadow_color": "#00000080",
            "shadow_blur": 0,
            "shadow_offset_x": 0,
            "shadow_offset_y": 1,
        },
        "subtitle": {
            "enabled": True,
            "text": "A clear, focused workflow demonstration",
            "size": 14,
            "color": "#C4D0E4FF",
            "font": "",
            "bold": False,
            "italic": False,
            "stroke_width": 0,
            "stroke_color": "#000000FF",
            "shadow_color": "#00000080",
            "shadow_blur": 0,
            "shadow_offset_x": 0,
            "shadow_offset_y": 1,
        },
        "badge": {
            "enabled": True,
            "text": "DEMO",
            "size": 11,
            "color": "#FFFFFFFF",
            "background": "#2F70EEEB",
            "font": "",
            "bold": True,
            "italic": False,
            "position": "top_right",
            "width": 0,
            "height": 0,
            "padding_x": 10,
            "padding_y": 4,
            "corner_radius": 7,
            "border": "#6EA1FFFF",
            "border_width": 0,
            "offset_x": 0,
            "offset_y": 0,
            "shadow_color": "#00000070",
            "shadow_blur": 4,
            "shadow_offset_x": 0,
            "shadow_offset_y": 2,
        },
    },
    "selection": {
        "line_color": "#4C97FFFF",
        "line_width": 2,
        "dash_length": 9,
        "dash_gap": 6,
        "handle_color": "#FFFFFFFF",
        "handle_border": "#2F70EEFF",
        "handle_border_width": 2,
        "handle_size": 14,
        "handle_shape": "circle",
        "dim_color": "#00000099",
        "show_dimensions": True,
        "dimension_color": "#FFFFFFFF",
        "dimension_size": 12,
    },
    "application": {
        "always_on_top": True,
        "minimize_to_tray": False,
        "theme": "dark",
    },
}


def merge_settings(base: dict[str, Any], override: dict[str, Any]) -> dict[str, Any]:
    """Recursively merge a JSON object without mutating either input."""

    result = copy.deepcopy(base)
    for key, value in override.items():
        if isinstance(value, dict) and isinstance(result.get(key), dict):
            result[key] = merge_settings(result[key], value)
        else:
            result[key] = copy.deepcopy(value)
    return result


def default_profile() -> dict[str, Any]:
    return copy.deepcopy(DEFAULT_PROFILE)


def _require_number(value: Any, name: str, minimum: float, maximum: float) -> None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{name} must be a number")
    if not minimum <= float(value) <= maximum:
        raise ValueError(f"{name} must be between {minimum:g} and {maximum:g}")


def _reject_unknown_fields(value: dict[str, Any], template: dict[str, Any], prefix: str = "") -> None:
    for key, item in value.items():
        path = f"{prefix}.{key}" if prefix else key
        if key not in template:
            raise ValueError(f"Unknown profile field: {path}")
        if isinstance(item, dict):
            if not isinstance(template[key], dict):
                raise ValueError(f"{path} must not be an object")
            _reject_unknown_fields(item, template[key], path)
        elif isinstance(template[key], dict):
            raise ValueError(f"{path} must be an object")


def _require_color(value: Any, name: str) -> None:
    if isinstance(value, str):
        text = value.removeprefix("#")
        if len(text) in {6, 8}:
            try:
                int(text, 16)
                return
            except ValueError:
                pass
    elif isinstance(value, list) and len(value) in {3, 4}:
        if all(isinstance(channel, int) and not isinstance(channel, bool) and 0 <= channel <= 255 for channel in value):
            return
    raise ValueError(f"{name} must be #RRGGBB, #RRGGBBAA, RGB, or RGBA")


def validate_profile(profile: dict[str, Any]) -> dict[str, Any]:
    """Return a complete profile or raise a precise validation error."""

    if not isinstance(profile, dict):
        raise ValueError("A profile must be a JSON object")
    _reject_unknown_fields(profile, DEFAULT_PROFILE)
    result = merge_settings(DEFAULT_PROFILE, profile)
    capture = result["capture"]
    output = result["output"]
    caption = result["caption"]
    selection = result["selection"]

    if capture["mode"] not in {"monitor", "region"}:
        raise ValueError("capture.mode must be 'monitor' or 'region'")
    if not isinstance(capture["monitor"], int) or capture["monitor"] < 1:
        raise ValueError("capture.monitor must be a positive monitor number")
    region = capture.get("region")
    if region is not None:
        if not isinstance(region, list) or len(region) != 4 or not all(isinstance(item, int) for item in region):
            raise ValueError("capture.region must be null or [x, y, width, height]")
        if region[2] < 16 or region[3] < 16:
            raise ValueError("The saved capture region is too small")
        if region[0] < 0 or region[1] < 0:
            raise ValueError("The saved capture region origin must not be negative")
    _require_number(capture["recording_fps"], "capture.recording_fps", 1, 120)
    _require_number(capture["gif_fps"], "capture.gif_fps", 1, 60)
    _require_number(capture["countdown_seconds"], "capture.countdown_seconds", 0, 10)
    _require_number(capture["maximum_duration_seconds"], "capture.maximum_duration_seconds", 0, 86400)
    _require_number(capture["region_aspect_width"], "capture.region_aspect_width", 1, 1000)
    _require_number(capture["region_aspect_height"], "capture.region_aspect_height", 1, 1000)
    _require_number(capture["region_minimum_size"], "capture.region_minimum_size", 16, 1000)
    for key in ("toggle_hotkey", "pause_hotkey", "cancel_hotkey"):
        if not isinstance(capture[key], str) or not capture[key].strip():
            raise ValueError(f"capture.{key} must not be empty")

    if not isinstance(output["directory"], str) or not output["directory"].strip():
        raise ValueError("output.directory must not be empty")
    if not isinstance(output["filename_template"], str) or not output["filename_template"].strip():
        raise ValueError("output.filename_template must not be empty")
    try:
        output["filename_template"].format(date="date", time="time", title="title", counter="1")
    except KeyError as error:
        raise ValueError(f"Unknown filename placeholder: {error.args[0]}") from error
    except ValueError as error:
        raise ValueError(f"Invalid filename template: {error}") from error
    _require_number(output["width"], "output.width", 64, 7680)
    _require_number(output["palette_colors"], "output.palette_colors", 2, 256)
    _require_number(output["frame_step"], "output.frame_step", 1, 30)
    _require_number(output["loop"], "output.loop", 0, 10000)
    _require_number(output["final_frame_duration_ms"], "output.final_frame_duration_ms", 0, 60000)

    if caption["anchor"] not in {
        "top_left", "top_center", "top_right", "center_left", "center", "center_right",
        "bottom_left", "bottom_center", "bottom_right",
    }:
        raise ValueError("caption.anchor is invalid")
    if caption["text_alignment"] not in {"left", "center", "right"}:
        raise ValueError("caption.text_alignment is invalid")
    _require_number(caption["width"], "caption.width", 80, 7680)
    for key in ("offset_x", "offset_y"):
        _require_number(caption[key], f"caption.{key}", -7680, 7680)
    for key in ("padding_x", "padding_y", "line_gap"):
        _require_number(caption[key], f"caption.{key}", 0, 500)
    _require_number(caption["border_width"], "caption.border_width", 0, 30)
    _require_number(caption["corner_radius"], "caption.corner_radius", 0, 500)
    _require_number(caption["background_blur"], "caption.background_blur", 0, 100)
    _require_number(caption["shadow_blur"], "caption.shadow_blur", 0, 100)
    for key in ("shadow_offset_x", "shadow_offset_y"):
        _require_number(caption[key], f"caption.{key}", -100, 100)
    for key in ("background", "border", "shadow_color"):
        _require_color(caption[key], f"caption.{key}")
    for name in ("title", "subtitle"):
        text = caption[name]
        if not isinstance(text["text"], str) or not isinstance(text["font"], str):
            raise ValueError(f"caption.{name} text and font must be strings")
        _require_number(text["size"], f"caption.{name}.size", 6, 300)
        _require_number(text["stroke_width"], f"caption.{name}.stroke_width", 0, 30)
        _require_number(text["shadow_blur"], f"caption.{name}.shadow_blur", 0, 100)
        for key in ("shadow_offset_x", "shadow_offset_y"):
            _require_number(text[key], f"caption.{name}.{key}", -100, 100)
        for key in ("color", "stroke_color", "shadow_color"):
            _require_color(text[key], f"caption.{name}.{key}")
    badge = caption["badge"]
    if not isinstance(badge["text"], str) or not isinstance(badge["font"], str):
        raise ValueError("caption.badge text and font must be strings")
    if badge["position"] not in {"top_left", "top_center", "top_right", "inside_left", "inside_center", "inside_right"}:
        raise ValueError("caption.badge.position is invalid")
    for key, minimum, maximum in (
        ("size", 6, 300), ("width", 0, 2000), ("height", 0, 1000),
        ("padding_x", 0, 200), ("padding_y", 0, 200), ("corner_radius", 0, 500),
        ("border_width", 0, 30), ("offset_x", -2000, 2000), ("offset_y", -2000, 2000),
        ("shadow_blur", 0, 100), ("shadow_offset_x", -100, 100), ("shadow_offset_y", -100, 100),
    ):
        _require_number(badge[key], f"caption.badge.{key}", minimum, maximum)
    for key in ("color", "background", "border", "shadow_color"):
        _require_color(badge[key], f"caption.badge.{key}")
    _require_number(selection["line_width"], "selection.line_width", 1, 20)
    _require_number(selection["dash_length"], "selection.dash_length", 1, 100)
    _require_number(selection["dash_gap"], "selection.dash_gap", 1, 100)
    _require_number(selection["handle_border_width"], "selection.handle_border_width", 1, 20)
    _require_number(selection["handle_size"], "selection.handle_size", 6, 80)
    _require_number(selection["dimension_size"], "selection.dimension_size", 8, 72)
    for key in ("line_color", "handle_color", "handle_border", "dim_color", "dimension_color"):
        _require_color(selection[key], f"selection.{key}")
    if selection["handle_shape"] not in {"circle", "square"}:
        raise ValueError("selection.handle_shape must be 'circle' or 'square'")
    if result["application"]["theme"] not in {"dark", "light", "system"}:
        raise ValueError("application.theme must be 'dark', 'light', or 'system'")
    boolean_fields = (
        (capture, "capture", ("capture_cursor", "region_lock_aspect", "region_snap_to_edges")),
        (output, "output", ("dither", "save_source_video", "open_folder_after_save")),
        (caption, "caption", ("enabled",)),
        (caption["title"], "caption.title", ("enabled", "bold", "italic")),
        (caption["subtitle"], "caption.subtitle", ("enabled", "bold", "italic")),
        (badge, "caption.badge", ("enabled", "bold", "italic")),
        (selection, "selection", ("show_dimensions",)),
        (result["application"], "application", ("always_on_top", "minimize_to_tray")),
    )
    for owner, prefix, keys in boolean_fields:
        for key in keys:
            if not isinstance(owner[key], bool):
                raise ValueError(f"{prefix}.{key} must be true or false")
    return result


def app_data_directory() -> Path:
    local = os.environ.get("LOCALAPPDATA")
    if local:
        return Path(local) / "Screen Demo Recorder"
    return Path.home() / ".screen-demo-recorder"


class SettingsStore:
    """Own the settings document and atomic profile operations."""

    def __init__(self, path: str | Path | None = None) -> None:
        self.path = Path(path) if path else app_data_directory() / "settings.json"
        self.data: dict[str, Any] = {}
        self.load()

    @property
    def profile_names(self) -> list[str]:
        return list(self.data["profiles"])

    @property
    def active_name(self) -> str:
        return self.data["active_profile"]

    @property
    def active_profile(self) -> dict[str, Any]:
        return copy.deepcopy(self.data["profiles"][self.active_name])

    @property
    def recent_files(self) -> list[str]:
        return list(self.data.get("recent_files", []))

    def load(self) -> None:
        if not self.path.is_file():
            self.data = {
                "schema_version": SCHEMA_VERSION,
                "active_profile": DEFAULT_PROFILE_NAME,
                "profiles": {DEFAULT_PROFILE_NAME: default_profile()},
                "recent_files": [],
            }
            return
        raw = json.loads(self.path.read_text(encoding="utf-8"))
        if raw.get("schema_version") != SCHEMA_VERSION:
            raise ValueError(f"Unsupported settings schema: {raw.get('schema_version')!r}")
        profiles = raw.get("profiles")
        if not isinstance(profiles, dict) or not profiles:
            raise ValueError("Settings must contain at least one profile")
        validated = {}
        for name, profile in profiles.items():
            if not isinstance(name, str) or not name.strip():
                raise ValueError("Profile names must not be empty")
            validated[name] = validate_profile(profile)
        active = raw.get("active_profile")
        if active not in validated:
            active = next(iter(validated))
        recent = raw.get("recent_files", [])
        self.data = {
            "schema_version": SCHEMA_VERSION,
            "active_profile": active,
            "profiles": validated,
            "recent_files": [str(item) for item in recent if isinstance(item, str)][:10],
        }

    def save(self) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_suffix(self.path.suffix + ".tmp")
        temporary.write_text(json.dumps(self.data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        os.replace(temporary, self.path)

    def update_active(self, profile: dict[str, Any]) -> None:
        self.data["profiles"][self.active_name] = validate_profile(profile)
        self.save()

    def activate(self, name: str) -> None:
        if name not in self.data["profiles"]:
            raise KeyError(name)
        self.data["active_profile"] = name
        self.save()

    def save_as(self, name: str, profile: dict[str, Any]) -> None:
        clean = name.strip()
        if not clean:
            raise ValueError("Profile name must not be empty")
        self.data["profiles"][clean] = validate_profile(profile)
        self.data["active_profile"] = clean
        self.save()

    def delete(self, name: str) -> None:
        if len(self.data["profiles"]) == 1:
            raise ValueError("The last profile cannot be deleted")
        del self.data["profiles"][name]
        if self.active_name == name:
            self.data["active_profile"] = next(iter(self.data["profiles"]))
        self.save()

    def reset_active(self) -> None:
        self.data["profiles"][self.active_name] = default_profile()
        self.save()

    def export_active(self, path: str | Path) -> Path:
        destination = Path(path)
        payload = {
            "schema_version": SCHEMA_VERSION,
            "name": self.active_name,
            "profile": self.active_profile,
        }
        destination.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        return destination

    def import_profile(self, path: str | Path) -> str:
        payload = json.loads(Path(path).read_text(encoding="utf-8"))
        if payload.get("schema_version") != SCHEMA_VERSION:
            raise ValueError("The profile uses an unsupported schema")
        name = str(payload.get("name", "Imported")).strip() or "Imported"
        base = name
        counter = 2
        while name in self.data["profiles"]:
            name = f"{base} {counter}"
            counter += 1
        self.save_as(name, payload.get("profile"))
        return name

    def add_recent_file(self, path: str | Path) -> None:
        value = str(Path(path).resolve())
        current = [item for item in self.recent_files if item.casefold() != value.casefold()]
        self.data["recent_files"] = [value, *current][:10]
        self.save()
