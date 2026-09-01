# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""PySide6 desktop interface for Screen Demo Recorder."""

from __future__ import annotations

import copy
import logging
import shutil
import tempfile
import threading
from datetime import datetime
from pathlib import Path
from typing import Any
from uuid import uuid4

from PySide6.QtCore import QObject, QTimer, QUrl, Qt, Signal
from PySide6.QtGui import QAction, QColor, QCloseEvent, QDesktopServices, QIcon
from PySide6.QtWidgets import (
    QApplication,
    QCheckBox,
    QColorDialog,
    QComboBox,
    QDoubleSpinBox,
    QFileDialog,
    QFormLayout,
    QFrame,
    QGroupBox,
    QHBoxLayout,
    QInputDialog,
    QLabel,
    QLineEdit,
    QMainWindow,
    QMenu,
    QMessageBox,
    QPushButton,
    QScrollArea,
    QSizePolicy,
    QSpinBox,
    QStyle,
    QSystemTrayIcon,
    QTabWidget,
    QVBoxLayout,
    QWidget,
)

from . import __version__
from .overlays import CountdownOverlay, PreviewOverlay, RegionSelector
from .pipeline import ProcessingResult, process_recording
from .recording import ScreenRecorder
from .settings import SettingsStore, validate_profile
from .windows import Monitor, exclude_window_from_capture, list_monitors


LOGGER = logging.getLogger(__name__)


DARK_STYLE = """
QWidget { background: #111722; color: #e8edf7; font-family: 'Segoe UI'; font-size: 10pt; }
QMainWindow { background: #0c111a; }
QGroupBox { border: 1px solid #293449; border-radius: 7px; margin-top: 12px; padding: 10px 8px 8px; font-weight: 600; }
QGroupBox::title { subcontrol-origin: margin; left: 10px; padding: 0 5px; color: #9fc0ff; }
QLineEdit, QSpinBox, QDoubleSpinBox, QComboBox { background: #1a2230; border: 1px solid #35435b; border-radius: 5px; padding: 6px; min-height: 20px; }
QLineEdit:focus, QSpinBox:focus, QDoubleSpinBox:focus, QComboBox:focus { border-color: #4c97ff; }
QPushButton { background: #243149; border: 1px solid #3c4f70; border-radius: 6px; padding: 7px 12px; }
QPushButton:hover { background: #2d3d59; border-color: #5c78a5; }
QPushButton:pressed { background: #1c2739; }
QPushButton#recordButton { background: #c43d52; border-color: #eb6376; color: white; font-weight: 700; min-height: 28px; }
QPushButton#recordButton[recording="true"] { background: #2d8b63; border-color: #54c795; }
QPushButton:disabled { color: #667085; background: #171d28; border-color: #263043; }
QTabWidget::pane { border: 1px solid #293449; border-radius: 6px; }
QTabBar::tab { background: #161e2b; border: 1px solid #293449; padding: 8px 12px; }
QTabBar::tab:selected { background: #243149; color: #a9c7ff; }
QScrollArea { border: none; }
QToolTip { background: #202a3a; color: white; border: 1px solid #4c97ff; }
"""

LIGHT_STYLE = """
QWidget { background: #f4f6fa; color: #1b2433; font-family: 'Segoe UI'; font-size: 10pt; }
QMainWindow { background: #e9edf4; }
QGroupBox { border: 1px solid #c9d1df; border-radius: 7px; margin-top: 12px; padding: 10px 8px 8px; font-weight: 600; }
QGroupBox::title { subcontrol-origin: margin; left: 10px; padding: 0 5px; color: #245eac; }
QLineEdit, QSpinBox, QDoubleSpinBox, QComboBox { background: white; border: 1px solid #b8c3d5; border-radius: 5px; padding: 6px; min-height: 20px; }
QLineEdit:focus, QSpinBox:focus, QDoubleSpinBox:focus, QComboBox:focus { border-color: #327bd6; }
QPushButton { background: #e2e8f2; border: 1px solid #b5c1d4; border-radius: 6px; padding: 7px 12px; }
QPushButton:hover { background: #d6dfed; border-color: #8fa1bd; }
QPushButton#recordButton { background: #c83e54; border-color: #ac2d42; color: white; font-weight: 700; min-height: 28px; }
QPushButton#recordButton[recording="true"] { background: #2d8b63; border-color: #22734f; }
QPushButton:disabled { color: #8c96a6; background: #edf0f5; border-color: #d6dbe4; }
QTabWidget::pane { border: 1px solid #c9d1df; border-radius: 6px; }
QTabBar::tab { background: #e7ebf2; border: 1px solid #c9d1df; padding: 8px 12px; }
QTabBar::tab:selected { background: white; color: #245eac; }
QScrollArea { border: none; }
"""


def _nested_get(data: dict[str, Any], path: str) -> Any:
    value: Any = data
    for part in path.split("."):
        value = value[part]
    return value


def _nested_set(data: dict[str, Any], path: str, value: Any) -> None:
    parts = path.split(".")
    target = data
    for part in parts[:-1]:
        target = target[part]
    target[parts[-1]] = value


class ColorEdit(QWidget):
    changed = Signal(str)

    def __init__(self) -> None:
        super().__init__()
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(5)
        self.edit = QLineEdit()
        self.button = QPushButton("Pick")
        self.button.setFixedWidth(54)
        layout.addWidget(self.edit)
        layout.addWidget(self.button)
        self.edit.textChanged.connect(self.changed)
        self.button.clicked.connect(self._pick)

    def value(self) -> str:
        return self.edit.text()

    def setValue(self, value: str | list[int]) -> None:
        if isinstance(value, list):
            channels = [*value, 255] if len(value) == 3 else value
            self.edit.setText("#" + "".join(f"{int(channel):02X}" for channel in channels))
        else:
            self.edit.setText(str(value))

    def _pick(self) -> None:
        current = QColor(self.edit.text())
        selected = QColorDialog.getColor(current, self, "Choose Color", QColorDialog.ShowAlphaChannel)
        if selected.isValid():
            self.edit.setText(f"#{selected.red():02X}{selected.green():02X}{selected.blue():02X}{selected.alpha():02X}")


class HotkeySignals(QObject):
    toggle = Signal()
    pause = Signal()
    cancel = Signal()


class GlobalHotkeys:
    def __init__(self, signals: HotkeySignals) -> None:
        self.signals = signals
        self.listener = None

    def start(self, capture: dict[str, Any]) -> None:
        self.stop()
        from pynput import keyboard

        values = {
            str(capture["toggle_hotkey"]): self.signals.toggle.emit,
            str(capture["pause_hotkey"]): self.signals.pause.emit,
            str(capture["cancel_hotkey"]): self.signals.cancel.emit,
        }
        normalized = [key.casefold().strip() for key in values]
        if len(set(normalized)) != len(normalized):
            raise ValueError("Record, pause, and cancel hotkeys must be different")
        for value in values:
            keyboard.HotKey.parse(value)
        self.listener = keyboard.GlobalHotKeys(values)
        self.listener.start()

    def stop(self) -> None:
        if self.listener is not None:
            self.listener.stop()
            self.listener = None


class ProcessingSignals(QObject):
    completed = Signal(object)
    failed = Signal(str)


class MainWindow(QMainWindow):
    def __init__(self, store: SettingsStore) -> None:
        super().__init__()
        self.store = store
        self.profile = store.active_profile
        self.monitors: list[Monitor] = list_monitors()
        if not self.monitors:
            raise RuntimeError("No active monitors were found")
        self.fields: dict[str, QWidget] = {}
        self.loading = False
        self.state = "ready"
        self.recorded_at: datetime | None = None
        self.profile_snapshot: dict[str, Any] | None = None
        self.capture_rectangle: tuple[int, int, int, int] | None = None
        self.capture_monitor: Monitor | None = None
        self.recorder = ScreenRecorder()
        self.preview_overlay = PreviewOverlay()
        self.countdown_overlay = CountdownOverlay()
        self.countdown_timer = QTimer(self)
        self.countdown_timer.setInterval(1000)
        self.countdown_timer.timeout.connect(self._countdown_tick)
        self.countdown_value = 0
        self.heartbeat = QTimer(self)
        self.heartbeat.setInterval(100)
        self.heartbeat.timeout.connect(self._heartbeat)
        self.heartbeat.start()
        self.save_timer = QTimer(self)
        self.save_timer.setSingleShot(True)
        self.save_timer.setInterval(350)
        self.save_timer.timeout.connect(self._save_fields)
        self.hotkey_signals = HotkeySignals()
        self.hotkey_signals.toggle.connect(self.toggle_recording)
        self.hotkey_signals.pause.connect(self.toggle_pause)
        self.hotkey_signals.cancel.connect(self.cancel_recording)
        self.hotkeys = GlobalHotkeys(self.hotkey_signals)
        self.processing_signals = ProcessingSignals()
        self.processing_signals.completed.connect(self._processing_completed)
        self.processing_signals.failed.connect(self._processing_failed)
        self._capture_exclusion_applied = False
        self._quitting = False

        self.setWindowTitle(f"Screen Demo Recorder {__version__}")
        self.resize(680, 820)
        self.setMinimumSize(560, 620)
        self._build_ui()
        self._create_tray()
        self._load_profile()

    def _build_ui(self) -> None:
        central = QWidget()
        root = QVBoxLayout(central)
        root.setContentsMargins(14, 14, 14, 14)
        root.setSpacing(10)

        profile_row = QHBoxLayout()
        profile_row.addWidget(QLabel("Profile"))
        self.profile_combo = QComboBox()
        self.profile_combo.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)
        profile_row.addWidget(self.profile_combo, 1)
        for label, callback in (
            ("Save As", self._save_profile_as),
            ("Import", self._import_profile),
            ("Export", self._export_profile),
            ("Reset", self._reset_profile),
            ("Delete", self._delete_profile),
        ):
            button = QPushButton(label)
            button.clicked.connect(callback)
            profile_row.addWidget(button)
        root.addLayout(profile_row)

        status_card = QFrame()
        status_card.setFrameShape(QFrame.StyledPanel)
        status_layout = QVBoxLayout(status_card)
        status_top = QHBoxLayout()
        self.status_label = QLabel("Ready")
        self.status_label.setStyleSheet("font-size: 13pt; font-weight: 700; color: #8fb7ff;")
        self.duration_label = QLabel("00:00.0")
        self.duration_label.setStyleSheet("font-family: Consolas; font-size: 15pt;")
        status_top.addWidget(self.status_label)
        status_top.addStretch()
        status_top.addWidget(self.duration_label)
        status_layout.addLayout(status_top)
        button_row = QHBoxLayout()
        self.record_button = QPushButton("Record")
        self.record_button.setObjectName("recordButton")
        self.record_button.clicked.connect(self.toggle_recording)
        self.pause_button = QPushButton("Pause")
        self.pause_button.clicked.connect(self.toggle_pause)
        self.cancel_button = QPushButton("Cancel")
        self.cancel_button.clicked.connect(self.cancel_recording)
        self.region_button = QPushButton("Select Region")
        self.region_button.clicked.connect(self.select_region)
        self.preview_button = QPushButton("Show Preview")
        self.preview_button.setCheckable(True)
        self.preview_button.toggled.connect(self._preview_toggled)
        for button in (self.record_button, self.pause_button, self.cancel_button, self.region_button, self.preview_button):
            button_row.addWidget(button)
        status_layout.addLayout(button_row)
        root.addWidget(status_card)

        self.tabs = QTabWidget()
        self.tabs.addTab(self._capture_tab(), "Capture")
        self.tabs.addTab(self._output_tab(), "Output")
        self.tabs.addTab(self._caption_tab(), "Caption")
        self.tabs.addTab(self._selection_tab(), "Selection")
        root.addWidget(self.tabs, 1)

        bottom = QHBoxLayout()
        self.region_label = QLabel("Region: full monitor")
        self.region_label.setStyleSheet("color: #8b98ad;")
        bottom.addWidget(self.region_label, 1)
        about = QPushButton("About")
        about.clicked.connect(self._show_about)
        bottom.addWidget(about)
        root.addLayout(bottom)
        self.setCentralWidget(central)

        self.profile_combo.currentTextChanged.connect(self._profile_selected)
        self.pause_button.setEnabled(False)
        self.cancel_button.setEnabled(False)

    def _scroll(self, groups: list[QWidget]) -> QScrollArea:
        content = QWidget()
        layout = QVBoxLayout(content)
        layout.setContentsMargins(10, 10, 10, 10)
        for group in groups:
            layout.addWidget(group)
        layout.addStretch()
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(content)
        return scroll

    def _group(self, title: str) -> tuple[QGroupBox, QFormLayout]:
        group = QGroupBox(title)
        form = QFormLayout(group)
        form.setFieldGrowthPolicy(QFormLayout.ExpandingFieldsGrow)
        form.setLabelAlignment(Qt.AlignRight | Qt.AlignVCenter)
        return group, form

    def _bind(self, form: QFormLayout, path: str, label: str, widget: QWidget) -> QWidget:
        self.fields[path] = widget
        form.addRow(label, widget)
        if isinstance(widget, QLineEdit):
            widget.textChanged.connect(self._field_changed)
        elif isinstance(widget, (QSpinBox, QDoubleSpinBox)):
            widget.valueChanged.connect(self._field_changed)
        elif isinstance(widget, QCheckBox):
            widget.toggled.connect(self._field_changed)
        elif isinstance(widget, QComboBox):
            widget.currentIndexChanged.connect(self._field_changed)
        elif isinstance(widget, ColorEdit):
            widget.changed.connect(self._field_changed)
        return widget

    def _combo(self, entries: list[tuple[str, Any]]) -> QComboBox:
        widget = QComboBox()
        for label, value in entries:
            widget.addItem(label, value)
        return widget

    def _spin(self, minimum: int, maximum: int, suffix: str = "") -> QSpinBox:
        widget = QSpinBox()
        widget.setRange(minimum, maximum)
        if suffix:
            widget.setSuffix(suffix)
        return widget

    def _double(self, minimum: float, maximum: float, suffix: str = "") -> QDoubleSpinBox:
        widget = QDoubleSpinBox()
        widget.setRange(minimum, maximum)
        widget.setDecimals(1)
        if suffix:
            widget.setSuffix(suffix)
        return widget

    def _capture_tab(self) -> QScrollArea:
        capture, form = self._group("Capture Source")
        mode = self._combo([("Full monitor", "monitor"), ("Selected region", "region")])
        self._bind(form, "capture.mode", "Mode", mode)
        monitor = QComboBox()
        for item in self.monitors:
            monitor.addItem(item.label, item.index)
        self._bind(form, "capture.monitor", "Monitor", monitor)
        monitor.currentIndexChanged.connect(self._monitor_changed)
        self._bind(form, "capture.recording_fps", "Recording FPS", self._double(1, 120, " fps"))
        self._bind(form, "capture.gif_fps", "GIF FPS", self._double(1, 60, " fps"))
        self._bind(form, "capture.capture_cursor", "Capture cursor", QCheckBox())
        self._bind(form, "capture.countdown_seconds", "Countdown", self._spin(0, 10, " s"))
        self._bind(form, "capture.maximum_duration_seconds", "Maximum duration", self._spin(0, 86400, " s"))

        region, region_form = self._group("Region Geometry")
        self.region_x = self._spin(0, 32768, " px")
        self.region_y = self._spin(0, 32768, " px")
        self.region_width = self._spin(16, 32768, " px")
        self.region_height = self._spin(16, 32768, " px")
        region_form.addRow("X", self.region_x)
        region_form.addRow("Y", self.region_y)
        region_form.addRow("Width", self.region_width)
        region_form.addRow("Height", self.region_height)
        for widget in (self.region_x, self.region_y, self.region_width, self.region_height):
            widget.valueChanged.connect(self._region_numbers_changed)
        self._bind(region_form, "capture.region_lock_aspect", "Lock aspect ratio", QCheckBox())
        self._bind(region_form, "capture.region_aspect_width", "Aspect width", self._spin(1, 1000))
        self._bind(region_form, "capture.region_aspect_height", "Aspect height", self._spin(1, 1000))
        self._bind(region_form, "capture.region_snap_to_edges", "Snap to monitor edges", QCheckBox())
        self._bind(region_form, "capture.region_minimum_size", "Minimum size", self._spin(16, 1000, " px"))

        hotkeys, hotkey_form = self._group("Global Hotkeys")
        self._bind(hotkey_form, "capture.toggle_hotkey", "Record / Stop", QLineEdit())
        self._bind(hotkey_form, "capture.pause_hotkey", "Pause / Resume", QLineEdit())
        self._bind(hotkey_form, "capture.cancel_hotkey", "Cancel", QLineEdit())
        help_label = QLabel("Use pynput syntax, for example <ctrl>+<shift>+<f9>.")
        help_label.setWordWrap(True)
        help_label.setStyleSheet("color: #8491a6;")
        hotkey_form.addRow("", help_label)

        application, app_form = self._group("Application")
        self._bind(app_form, "application.always_on_top", "Always on top", QCheckBox())
        self._bind(app_form, "application.minimize_to_tray", "Close to tray", QCheckBox())
        self._bind(app_form, "application.theme", "Theme", self._combo([("Dark", "dark"), ("Light", "light"), ("System", "system")]))
        return self._scroll([capture, region, hotkeys, application])

    def _output_tab(self) -> QScrollArea:
        destination, form = self._group("Destination")
        path_row = QWidget()
        row = QHBoxLayout(path_row)
        row.setContentsMargins(0, 0, 0, 0)
        self.output_directory = QLineEdit()
        browse = QPushButton("Browse")
        browse.clicked.connect(self._browse_output)
        row.addWidget(self.output_directory, 1)
        row.addWidget(browse)
        self.fields["output.directory"] = self.output_directory
        self.output_directory.textChanged.connect(self._field_changed)
        form.addRow("GIF folder", path_row)
        self._bind(form, "output.filename_template", "Filename template", QLineEdit())
        buttons = QWidget()
        button_row = QHBoxLayout(buttons)
        button_row.setContentsMargins(0, 0, 0, 0)
        open_folder = QPushButton("Open Folder")
        open_folder.clicked.connect(self._open_output_folder)
        open_last = QPushButton("Open Last GIF")
        open_last.clicked.connect(self._open_last_file)
        button_row.addWidget(open_folder)
        button_row.addWidget(open_last)
        form.addRow("", buttons)
        self.recent_combo = QComboBox()
        form.addRow("Recent GIFs", self.recent_combo)

        gif, gif_form = self._group("GIF Encoding")
        self._bind(gif_form, "output.width", "Width", self._spin(64, 7680, " px"))
        self._bind(gif_form, "output.palette_colors", "Palette", self._spin(2, 256, " colors"))
        self._bind(gif_form, "output.dither", "Dithering", QCheckBox())
        self._bind(gif_form, "output.loop", "Loop count", self._spin(0, 10000))
        self._bind(gif_form, "output.frame_step", "Keep every Nth frame", self._spin(1, 30))
        self._bind(gif_form, "output.final_frame_duration_ms", "Final frame", self._spin(0, 60000, " ms"))
        self._bind(gif_form, "output.save_source_video", "Keep source MP4", QCheckBox())
        self._bind(gif_form, "output.open_folder_after_save", "Open folder after save", QCheckBox())
        self.estimate_label = QLabel()
        self.estimate_label.setWordWrap(True)
        self.estimate_label.setStyleSheet("color: #8491a6;")
        gif_form.addRow("Estimate", self.estimate_label)
        return self._scroll([destination, gif])

    def _caption_tab(self) -> QScrollArea:
        container, form = self._group("Caption Container")
        self._bind(form, "caption.enabled", "Enabled", QCheckBox())
        self._bind(form, "caption.anchor", "Anchor", self._combo([
            ("Top left", "top_left"), ("Top center", "top_center"), ("Top right", "top_right"),
            ("Center left", "center_left"), ("Center", "center"), ("Center right", "center_right"),
            ("Bottom left", "bottom_left"), ("Bottom center", "bottom_center"), ("Bottom right", "bottom_right"),
        ]))
        self._bind(form, "caption.text_alignment", "Text alignment", self._combo([("Left", "left"), ("Center", "center"), ("Right", "right")]))
        self._bind(form, "caption.width", "Width", self._spin(80, 7680, " px"))
        self._bind(form, "caption.offset_x", "Offset X", self._spin(-7680, 7680, " px"))
        self._bind(form, "caption.offset_y", "Offset Y", self._spin(-7680, 7680, " px"))
        self._bind(form, "caption.padding_x", "Horizontal padding", self._spin(0, 500, " px"))
        self._bind(form, "caption.padding_y", "Vertical padding", self._spin(0, 500, " px"))
        self._bind(form, "caption.line_gap", "Line gap", self._spin(0, 200, " px"))
        self._bind(form, "caption.background", "Background", ColorEdit())
        self._bind(form, "caption.background_blur", "Background blur", self._spin(0, 100, " px"))
        self._bind(form, "caption.border", "Border", ColorEdit())
        self._bind(form, "caption.border_width", "Border width", self._spin(0, 30, " px"))
        self._bind(form, "caption.corner_radius", "Corner radius", self._spin(0, 500, " px"))
        self._bind(form, "caption.shadow_color", "Shadow", ColorEdit())
        self._bind(form, "caption.shadow_blur", "Shadow blur", self._spin(0, 100, " px"))
        self._bind(form, "caption.shadow_offset_x", "Shadow offset X", self._spin(-100, 100, " px"))
        self._bind(form, "caption.shadow_offset_y", "Shadow offset Y", self._spin(-100, 100, " px"))

        title = self._text_group("Title", "caption.title")
        subtitle = self._text_group("Subtitle", "caption.subtitle")

        badge, badge_form = self._group("Badge")
        self._bind(badge_form, "caption.badge.enabled", "Enabled", QCheckBox())
        self._bind(badge_form, "caption.badge.text", "Text", QLineEdit())
        self._bind(badge_form, "caption.badge.font", "Font file", QLineEdit())
        self._bind(badge_form, "caption.badge.size", "Font size", self._spin(6, 300, " px"))
        self._bind(badge_form, "caption.badge.bold", "Bold", QCheckBox())
        self._bind(badge_form, "caption.badge.italic", "Italic", QCheckBox())
        self._bind(badge_form, "caption.badge.position", "Position", self._combo([
            ("Above left", "top_left"), ("Above center", "top_center"), ("Above right", "top_right"),
            ("Inside left", "inside_left"), ("Inside center", "inside_center"), ("Inside right", "inside_right"),
        ]))
        self._bind(badge_form, "caption.badge.color", "Text color", ColorEdit())
        self._bind(badge_form, "caption.badge.background", "Background", ColorEdit())
        self._bind(badge_form, "caption.badge.width", "Fixed width", self._spin(0, 2000, " px"))
        self._bind(badge_form, "caption.badge.height", "Fixed height", self._spin(0, 1000, " px"))
        self._bind(badge_form, "caption.badge.padding_x", "Horizontal padding", self._spin(0, 200, " px"))
        self._bind(badge_form, "caption.badge.padding_y", "Vertical padding", self._spin(0, 200, " px"))
        self._bind(badge_form, "caption.badge.corner_radius", "Corner radius", self._spin(0, 500, " px"))
        self._bind(badge_form, "caption.badge.border", "Border", ColorEdit())
        self._bind(badge_form, "caption.badge.border_width", "Border width", self._spin(0, 30, " px"))
        self._bind(badge_form, "caption.badge.offset_x", "Offset X", self._spin(-2000, 2000, " px"))
        self._bind(badge_form, "caption.badge.offset_y", "Offset Y", self._spin(-2000, 2000, " px"))
        self._bind(badge_form, "caption.badge.shadow_color", "Shadow", ColorEdit())
        self._bind(badge_form, "caption.badge.shadow_blur", "Shadow blur", self._spin(0, 100, " px"))
        self._bind(badge_form, "caption.badge.shadow_offset_x", "Shadow offset X", self._spin(-100, 100, " px"))
        self._bind(badge_form, "caption.badge.shadow_offset_y", "Shadow offset Y", self._spin(-100, 100, " px"))
        return self._scroll([container, title, subtitle, badge])

    def _text_group(self, title: str, prefix: str) -> QGroupBox:
        group, form = self._group(title)
        self._bind(form, f"{prefix}.enabled", "Enabled", QCheckBox())
        self._bind(form, f"{prefix}.text", "Text", QLineEdit())
        self._bind(form, f"{prefix}.font", "Font file", QLineEdit())
        self._bind(form, f"{prefix}.size", "Font size", self._spin(6, 300, " px"))
        self._bind(form, f"{prefix}.bold", "Bold", QCheckBox())
        self._bind(form, f"{prefix}.italic", "Italic", QCheckBox())
        self._bind(form, f"{prefix}.color", "Color", ColorEdit())
        self._bind(form, f"{prefix}.stroke_width", "Outline width", self._spin(0, 30, " px"))
        self._bind(form, f"{prefix}.stroke_color", "Outline color", ColorEdit())
        self._bind(form, f"{prefix}.shadow_color", "Shadow", ColorEdit())
        self._bind(form, f"{prefix}.shadow_blur", "Shadow blur", self._spin(0, 100, " px"))
        self._bind(form, f"{prefix}.shadow_offset_x", "Shadow offset X", self._spin(-100, 100, " px"))
        self._bind(form, f"{prefix}.shadow_offset_y", "Shadow offset Y", self._spin(-100, 100, " px"))
        return group

    def _selection_tab(self) -> QScrollArea:
        line, form = self._group("Selection Lines")
        self._bind(form, "selection.line_color", "Line color", ColorEdit())
        self._bind(form, "selection.line_width", "Line width", self._spin(1, 20, " px"))
        self._bind(form, "selection.dash_length", "Dash length", self._spin(1, 100, " px"))
        self._bind(form, "selection.dash_gap", "Dash gap", self._spin(1, 100, " px"))
        self._bind(form, "selection.handle_color", "Point color", ColorEdit())
        self._bind(form, "selection.handle_border", "Point border", ColorEdit())
        self._bind(form, "selection.handle_border_width", "Point border width", self._spin(1, 20, " px"))
        self._bind(form, "selection.handle_size", "Point size", self._spin(6, 80, " px"))
        self._bind(form, "selection.handle_shape", "Point shape", self._combo([("Circle", "circle"), ("Square", "square")]))
        self._bind(form, "selection.dim_color", "Outside dimming", ColorEdit())
        self._bind(form, "selection.show_dimensions", "Show dimensions", QCheckBox())
        self._bind(form, "selection.dimension_color", "Dimension color", ColorEdit())
        self._bind(form, "selection.dimension_size", "Dimension size", self._spin(8, 72, " px"))
        hint = QLabel("The four points, dashed line, dimming, preview, countdown, and this application window are excluded from the recording.")
        hint.setWordWrap(True)
        hint.setStyleSheet("color: #8491a6;")
        form.addRow("", hint)
        return self._scroll([line])

    def _widget_value(self, widget: QWidget) -> Any:
        if isinstance(widget, QLineEdit):
            return widget.text()
        if isinstance(widget, (QSpinBox, QDoubleSpinBox)):
            return widget.value()
        if isinstance(widget, QCheckBox):
            return widget.isChecked()
        if isinstance(widget, QComboBox):
            return widget.currentData()
        if isinstance(widget, ColorEdit):
            return widget.value()
        raise TypeError(type(widget).__name__)

    def _set_widget_value(self, widget: QWidget, value: Any) -> None:
        if isinstance(widget, QLineEdit):
            widget.setText(str(value))
        elif isinstance(widget, (QSpinBox, QDoubleSpinBox)):
            widget.setValue(value)
        elif isinstance(widget, QCheckBox):
            widget.setChecked(bool(value))
        elif isinstance(widget, QComboBox):
            index = widget.findData(value)
            if index >= 0:
                widget.setCurrentIndex(index)
        elif isinstance(widget, ColorEdit):
            widget.setValue(str(value))
        else:
            raise TypeError(type(widget).__name__)

    def _load_profile(self) -> None:
        self.loading = True
        try:
            self.profile = self.store.active_profile
            self.profile_combo.clear()
            self.profile_combo.addItems(self.store.profile_names)
            self.profile_combo.setCurrentText(self.store.active_name)
            for path, widget in self.fields.items():
                self._set_widget_value(widget, _nested_get(self.profile, path))
            self._load_region_controls()
            self._apply_theme()
            self._apply_window_mode()
            self._update_region_label()
            self._refresh_recent_files()
            self._update_estimate()
        finally:
            self.loading = False
        self._restart_hotkeys(show_errors=True)
        if self.preview_button.isChecked():
            self._update_preview()

    def _field_changed(self, *_args) -> None:
        if self.loading:
            return
        self.save_timer.start()

    def _profile_from_fields(self) -> dict[str, Any]:
        profile = copy.deepcopy(self.profile)
        for path, widget in self.fields.items():
            _nested_set(profile, path, self._widget_value(widget))
        return validate_profile(profile)

    def _save_fields(self) -> None:
        if self.loading or self.state in {"countdown", "recording", "paused", "processing"}:
            return
        try:
            self.profile = self._profile_from_fields()
            self.store.update_active(self.profile)
            self._apply_theme()
            self._apply_window_mode()
            self._restart_hotkeys(show_errors=False)
            self._update_region_label()
            self._update_estimate()
            if self.preview_button.isChecked():
                self._update_preview()
        except Exception as error:
            self._status(f"Settings error: {error}", error=True)

    def _profile_selected(self, name: str) -> None:
        if self.loading or not name or name == self.store.active_name:
            return
        try:
            self.store.activate(name)
            self._load_profile()
        except Exception as error:
            QMessageBox.critical(self, "Profile Error", str(error))

    def _save_profile_as(self) -> None:
        name, accepted = QInputDialog.getText(self, "Save Profile As", "Profile name")
        if not accepted:
            return
        try:
            self.store.save_as(name, self._profile_from_fields())
            self._load_profile()
        except Exception as error:
            QMessageBox.critical(self, "Profile Error", str(error))

    def _delete_profile(self) -> None:
        try:
            self.store.delete(self.store.active_name)
            self._load_profile()
        except Exception as error:
            QMessageBox.critical(self, "Profile Error", str(error))

    def _reset_profile(self) -> None:
        if QMessageBox.question(self, "Reset Profile", "Reset every setting in the active profile?") != QMessageBox.Yes:
            return
        self.store.reset_active()
        self._load_profile()

    def _export_profile(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Export Profile", f"{self.store.active_name}.json", "JSON (*.json)")
        if path:
            try:
                self.store.export_active(path)
            except Exception as error:
                QMessageBox.critical(self, "Export Failed", str(error))

    def _import_profile(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "Import Profile", "", "JSON (*.json)")
        if path:
            try:
                self.store.import_profile(path)
                self._load_profile()
            except Exception as error:
                QMessageBox.critical(self, "Import Failed", str(error))

    def _monitor_changed(self) -> None:
        if self.loading:
            return
        self.profile["capture"]["region"] = None
        self.loading = True
        try:
            self._load_region_controls()
        finally:
            self.loading = False
        self._update_region_label()

    def _load_region_controls(self) -> None:
        monitor = self._selected_monitor()
        for widget, maximum in (
            (self.region_x, max(0, monitor.width - 16)),
            (self.region_y, max(0, monitor.height - 16)),
            (self.region_width, monitor.width),
            (self.region_height, monitor.height),
        ):
            widget.setMaximum(maximum)
        region = self.profile["capture"].get("region") or [0, 0, monitor.width, monitor.height]
        self.region_x.setValue(region[0])
        self.region_y.setValue(region[1])
        self.region_width.setValue(region[2])
        self.region_height.setValue(region[3])

    def _region_numbers_changed(self, *_args) -> None:
        if self.loading:
            return
        monitor = self._selected_monitor()
        self.loading = True
        try:
            x = min(self.region_x.value(), monitor.width - 16)
            y = min(self.region_y.value(), monitor.height - 16)
            width = min(self.region_width.value(), monitor.width - x)
            height = min(self.region_height.value(), monitor.height - y)
            if self.fields["capture.region_lock_aspect"].isChecked():
                ratio = self.fields["capture.region_aspect_width"].value() / self.fields["capture.region_aspect_height"].value()
                if self.sender() is self.region_width:
                    height = min(monitor.height - y, max(16, round(width / ratio)))
                elif self.sender() is self.region_height:
                    width = min(monitor.width - x, max(16, round(height * ratio)))
            self.region_x.setValue(x)
            self.region_y.setValue(y)
            self.region_width.setValue(width)
            self.region_height.setValue(height)
            self.profile["capture"]["region"] = [x, y, width, height]
            mode = self.fields["capture.mode"]
            index = mode.findData("region")
            if index >= 0:
                mode.setCurrentIndex(index)
        finally:
            self.loading = False
        self._update_region_label()
        self._update_estimate()
        self.save_timer.start()

    def _selected_monitor(self) -> Monitor:
        number = int(self._widget_value(self.fields["capture.monitor"]))
        return self._monitor_by_number(number)

    def _monitor_by_number(self, number: int) -> Monitor:
        for monitor in self.monitors:
            if monitor.index == number:
                return monitor
        raise RuntimeError("The selected monitor is no longer connected")

    def _target_rectangle(self, profile: dict[str, Any] | None = None) -> tuple[int, int, int, int]:
        current = profile or self.profile
        monitor_number = int(current["capture"]["monitor"])
        monitor = self._monitor_by_number(monitor_number)
        relative = current["capture"].get("region") if current["capture"]["mode"] == "region" else None
        return monitor.absolute_region(relative)

    def select_region(self) -> None:
        if self.state not in {"ready"}:
            return
        try:
            self._save_fields()
            monitor = self._selected_monitor()
            selector = RegionSelector(
                monitor,
                self.profile["capture"],
                self.profile["selection"],
                self.profile["capture"].get("region"),
                self,
            )
            if selector.exec() and selector.selected_region:
                self.profile["capture"]["region"] = selector.selected_region
                self.profile["capture"]["mode"] = "region"
                self.store.update_active(self.profile)
                self._load_profile()
        except Exception as error:
            QMessageBox.critical(self, "Region Selection Failed", str(error))

    def _update_region_label(self) -> None:
        if self.profile["capture"]["mode"] == "monitor":
            self.region_label.setText("Region: full monitor")
            return
        region = self.profile["capture"].get("region")
        self.region_label.setText(f"Region: {region[2]} × {region[3]} at {region[0]}, {region[1]}" if region else "Region: not selected")

    def _preview_toggled(self, enabled: bool) -> None:
        self.preview_button.setText("Hide Preview" if enabled else "Show Preview")
        if enabled:
            self._save_fields()
            self._update_preview()
        else:
            self.preview_overlay.hide()

    def _update_preview(self) -> None:
        if not self.preview_button.isChecked():
            return
        try:
            monitor = self._monitor_by_number(int(self.profile["capture"]["monitor"]))
            self.preview_overlay.show_for(self._target_rectangle(), monitor, self.profile["caption"])
        except Exception as error:
            self.preview_button.setChecked(False)
            QMessageBox.critical(self, "Preview Failed", str(error))

    def _browse_output(self) -> None:
        selected = QFileDialog.getExistingDirectory(self, "Choose GIF Folder", self.output_directory.text())
        if selected:
            self.output_directory.setText(selected)

    def _open_output_folder(self) -> None:
        path = Path(self.output_directory.text()).expanduser()
        path.mkdir(parents=True, exist_ok=True)
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(path.resolve())))

    def _open_last_file(self) -> None:
        selected = self.recent_combo.currentData() if self.recent_combo.count() else None
        if selected and Path(selected).is_file():
            QDesktopServices.openUrl(QUrl.fromLocalFile(str(Path(selected))))
            return
        for value in self.store.recent_files:
            path = Path(value)
            if path.is_file():
                QDesktopServices.openUrl(QUrl.fromLocalFile(str(path)))
                return
        QMessageBox.information(self, "No Recent GIF", "No recent GIF is available.")

    def _refresh_recent_files(self) -> None:
        if not hasattr(self, "recent_combo"):
            return
        self.recent_combo.clear()
        for value in self.store.recent_files:
            path = Path(value)
            self.recent_combo.addItem(path.name, value)

    def _update_estimate(self) -> None:
        if not hasattr(self, "estimate_label"):
            return
        try:
            profile = self._profile_from_fields() if not self.loading else self.profile
            duration = int(profile["capture"]["maximum_duration_seconds"])
            if duration <= 0:
                self.estimate_label.setText("Unlimited duration; final size cannot be estimated.")
                return
            source_width, source_height = self._target_rectangle(profile)[2:]
            width = int(profile["output"]["width"])
            height = round(source_height * width / source_width)
            frames = max(1, round(duration * float(profile["capture"]["gif_fps"]) / int(profile["output"]["frame_step"])))
            indexed_mib = width * height * frames / 1024 / 1024
            self.estimate_label.setText(f"Up to {frames:,} frames; roughly {indexed_mib:,.0f} MiB before GIF compression.")
        except Exception as error:
            self.estimate_label.setText(str(error))

    def _restart_hotkeys(self, *, show_errors: bool) -> None:
        if self.state not in {"ready"}:
            return
        try:
            self.hotkeys.start(self.profile["capture"])
        except Exception as error:
            self.hotkeys.stop()
            self._status(f"Hotkey error: {error}", error=True)
            if show_errors:
                QMessageBox.warning(self, "Global Hotkeys Disabled", str(error))

    def _preflight(self) -> bool:
        try:
            self._save_fields()
            self.profile = self._profile_from_fields()
            if self.profile["capture"]["mode"] == "region" and not self.profile["capture"].get("region"):
                self.select_region()
                if not self.profile["capture"].get("region"):
                    return False
            output = Path(self.profile["output"]["directory"]).expanduser().resolve()
            output.mkdir(parents=True, exist_ok=True)
            if shutil.disk_usage(output).free < 200 * 1024 * 1024:
                raise RuntimeError("At least 200 MiB of free disk space is required")
            self.capture_rectangle = self._target_rectangle(self.profile)
            self.capture_monitor = self._monitor_by_number(int(self.profile["capture"]["monitor"]))
            self.profile_snapshot = copy.deepcopy(self.profile)
            return True
        except Exception as error:
            QMessageBox.critical(self, "Cannot Start Recording", str(error))
            return False

    def toggle_recording(self) -> None:
        if self.state == "ready":
            if not self._preflight():
                return
            self._begin_countdown()
        elif self.state == "countdown":
            self.cancel_recording()
        elif self.state in {"recording", "paused"}:
            self._finish_recording()

    def _begin_countdown(self) -> None:
        seconds = int(self.profile_snapshot["capture"]["countdown_seconds"])
        if seconds <= 0:
            self._start_recording()
            return
        self.state = "countdown"
        self.countdown_value = seconds
        self._set_controls_for_state()
        self._status(f"Recording starts in {seconds}")
        self.countdown_overlay.show_value(self.capture_rectangle, self.capture_monitor, seconds)
        self.countdown_timer.start()

    def _countdown_tick(self) -> None:
        self.countdown_value -= 1
        if self.countdown_value <= 0:
            self.countdown_timer.stop()
            self.countdown_overlay.hide()
            self._start_recording()
        else:
            self.countdown_overlay.show_value(self.capture_rectangle, self.capture_monitor, self.countdown_value)
            self._status(f"Recording starts in {self.countdown_value}")

    def _start_recording(self) -> None:
        try:
            temporary_directory = Path(tempfile.gettempdir()) / "Screen Demo Recorder"
            temporary_directory.mkdir(parents=True, exist_ok=True)
            temporary = temporary_directory / f"recording-{uuid4().hex}.mp4"
            capture = self.profile_snapshot["capture"]
            self.recorder.start(
                self.capture_rectangle,
                temporary,
                fps=float(capture["recording_fps"]),
                capture_cursor=bool(capture["capture_cursor"]),
                maximum_duration_seconds=float(capture["maximum_duration_seconds"]),
            )
            self.recorded_at = datetime.now()
            self.state = "recording"
            self._set_controls_for_state()
            self._status("Recording")
        except Exception as error:
            self.state = "ready"
            self._set_controls_for_state()
            QMessageBox.critical(self, "Recording Failed", str(error))

    def toggle_pause(self) -> None:
        if self.state not in {"recording", "paused"}:
            return
        try:
            paused = self.recorder.toggle_pause()
            self.state = "paused" if paused else "recording"
            self._set_controls_for_state()
            self._status("Paused" if paused else "Recording")
        except Exception as error:
            QMessageBox.critical(self, "Pause Failed", str(error))

    def cancel_recording(self) -> None:
        if self.state == "countdown":
            self.countdown_timer.stop()
            self.countdown_overlay.hide()
        elif self.state in {"recording", "paused"}:
            self.recorder.cancel()
        else:
            return
        self.state = "ready"
        self.duration_label.setText("00:00.0")
        self._set_controls_for_state()
        self._status("Recording cancelled")

    def _finish_recording(self) -> None:
        if self.state not in {"recording", "paused"}:
            return
        self.state = "processing"
        self._set_controls_for_state()
        self._status("Processing GIF")
        try:
            source = self.recorder.stop()
        except Exception as error:
            self.state = "ready"
            self._set_controls_for_state()
            QMessageBox.critical(self, "Recording Failed", str(error))
            return
        profile = copy.deepcopy(self.profile_snapshot)
        recorded_at = self.recorded_at

        def worker() -> None:
            try:
                result = process_recording(source, profile, recorded_at=recorded_at)
                source.unlink(missing_ok=True)
                self.processing_signals.completed.emit(result)
            except Exception as error:
                LOGGER.exception("GIF processing failed")
                self.processing_signals.failed.emit(f"{error}\n\nThe recovery video remains at:\n{source}")

        threading.Thread(target=worker, name="GifProcessor", daemon=True).start()

    def _processing_completed(self, result: ProcessingResult) -> None:
        self.store.add_recent_file(result.gif)
        self._refresh_recent_files()
        self.state = "ready"
        self._set_controls_for_state()
        self._status(f"Saved {result.gif.name}")
        self.duration_label.setText("00:00.0")
        if self.tray.isVisible():
            self.tray.showMessage("Screen Demo Recorder", f"Saved {result.gif.name}", QSystemTrayIcon.Information, 4000)
        if self.profile_snapshot and self.profile_snapshot["output"].get("open_folder_after_save"):
            QDesktopServices.openUrl(QUrl.fromLocalFile(str(result.gif.parent)))

    def _processing_failed(self, message: str) -> None:
        self.state = "ready"
        self._set_controls_for_state()
        self._status("Processing failed", error=True)
        QMessageBox.critical(self, "GIF Processing Failed", message)

    def _heartbeat(self) -> None:
        if self.state in {"recording", "paused"}:
            seconds = self.recorder.active_seconds
            minutes, remainder = divmod(seconds, 60)
            self.duration_label.setText(f"{int(minutes):02d}:{remainder:04.1f}")
            if self.recorder.has_session and not self.recorder.is_recording:
                self._finish_recording()

    def _set_controls_for_state(self) -> None:
        active = self.state in {"recording", "paused"}
        busy = self.state in {"countdown", "recording", "paused", "processing"}
        self.record_button.setText("Stop" if active else ("Cancel Countdown" if self.state == "countdown" else "Record"))
        self.record_button.setProperty("recording", active)
        self.record_button.style().unpolish(self.record_button)
        self.record_button.style().polish(self.record_button)
        self.record_button.setEnabled(self.state != "processing")
        self.pause_button.setEnabled(active)
        self.pause_button.setText("Resume" if self.state == "paused" else "Pause")
        self.cancel_button.setEnabled(self.state in {"countdown", "recording", "paused"})
        self.region_button.setEnabled(not busy)
        self.tabs.setEnabled(not busy)
        self.profile_combo.setEnabled(not busy)

    def _status(self, text: str, *, error: bool = False) -> None:
        self.status_label.setText(text)
        self.status_label.setStyleSheet(f"font-size: 13pt; font-weight: 700; color: {'#ff7a8c' if error else '#8fb7ff'};")

    def _apply_theme(self) -> None:
        theme = self.profile["application"]["theme"]
        stylesheet = DARK_STYLE if theme == "dark" else (LIGHT_STYLE if theme == "light" else "")
        QApplication.instance().setStyleSheet(stylesheet)

    def _apply_window_mode(self) -> None:
        desired = bool(self.profile["application"]["always_on_top"])
        current = bool(self.windowFlags() & Qt.WindowStaysOnTopHint)
        if desired != current:
            self.setWindowFlag(Qt.WindowStaysOnTopHint, desired)
            self._capture_exclusion_applied = False
            self.show()
            QTimer.singleShot(0, self._ensure_capture_exclusion)

    def showEvent(self, event) -> None:
        super().showEvent(event)
        QTimer.singleShot(0, self._ensure_capture_exclusion)

    def _ensure_capture_exclusion(self) -> None:
        if self._capture_exclusion_applied:
            return
        try:
            exclude_window_from_capture(int(self.winId()))
            self._capture_exclusion_applied = True
        except Exception as error:
            self.record_button.setEnabled(False)
            QMessageBox.critical(self, "Capture Exclusion Failed", f"The application window cannot be excluded from recordings.\n\n{error}")

    def _create_tray(self) -> None:
        icon = self.style().standardIcon(QStyle.SP_MediaPlay)
        self.setWindowIcon(icon)
        self.tray = QSystemTrayIcon(icon, self)
        self.tray.setToolTip("Screen Demo Recorder")
        menu = QMenu(self)
        show_action = QAction("Show", self)
        show_action.triggered.connect(self._show_from_tray)
        record_action = QAction("Record / Stop", self)
        record_action.triggered.connect(self.toggle_recording)
        quit_action = QAction("Quit", self)
        quit_action.triggered.connect(self.quit_application)
        menu.addAction(show_action)
        menu.addAction(record_action)
        menu.addSeparator()
        menu.addAction(quit_action)
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(lambda reason: self._show_from_tray() if reason == QSystemTrayIcon.Trigger else None)
        if QSystemTrayIcon.isSystemTrayAvailable():
            self.tray.show()

    def _show_from_tray(self) -> None:
        self.show()
        self.raise_()
        self.activateWindow()

    def _show_about(self) -> None:
        QMessageBox.about(
            self,
            "About Screen Demo Recorder",
            f"<b>Screen Demo Recorder {__version__}</b><br><br>"
            "Records full monitors or selected regions on Windows 10 2004 and Windows 11, "
            "then builds captioned animated GIFs.<br><br>License: MIT",
        )

    def closeEvent(self, event: QCloseEvent) -> None:
        if self.state == "processing":
            self.show()
            QMessageBox.information(self, "GIF Processing", "Wait for GIF processing to finish before quitting.")
            event.ignore()
            return
        if not self._quitting and self.profile["application"].get("minimize_to_tray") and self.tray.isVisible():
            self.hide()
            event.ignore()
            self.tray.showMessage("Screen Demo Recorder", "The recorder is still running in the notification area.", QSystemTrayIcon.Information, 2500)
            return
        if self.state in {"countdown", "recording", "paused"}:
            if QMessageBox.question(self, "Quit Recorder", "Cancel the active recording and quit?") != QMessageBox.Yes:
                event.ignore()
                return
            self.cancel_recording()
        self._cleanup()
        event.accept()

    def _cleanup(self) -> None:
        self.hotkeys.stop()
        self.preview_overlay.close()
        self.countdown_overlay.close()
        self.tray.hide()

    def quit_application(self) -> None:
        if self.state == "processing":
            self._show_from_tray()
            QMessageBox.information(self, "GIF Processing", "Wait for GIF processing to finish before quitting.")
            return
        self._quitting = True
        if self.state in {"countdown", "recording", "paused"}:
            self.cancel_recording()
        self._cleanup()
        QApplication.quit()
