# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Capture-excluded region, preview, and countdown overlays."""

from __future__ import annotations

from typing import Any

from PIL.ImageQt import ImageQt
from PySide6.QtCore import QPointF, QRect, QRectF, Qt, Signal
from PySide6.QtGui import QColor, QFont, QGuiApplication, QImage, QMouseEvent, QPaintEvent, QPainter, QPen, QPixmap, QScreen
from PySide6.QtWidgets import QDialog, QWidget

from .render import color, render_caption_overlay
from .windows import Monitor, exclude_window_from_capture, list_monitors


def _qcolor(value: str | list[int]) -> QColor:
    red, green, blue, alpha = color(value)
    return QColor(red, green, blue, alpha)


def _screen_for_monitor(monitor: Monitor) -> QScreen:
    screens = QGuiApplication.screens()
    if not screens:
        raise RuntimeError("Qt did not report an active display")
    target_name = monitor.display_name.casefold().strip()
    named = [screen for screen in screens if screen.name().casefold().strip() == target_name]
    if target_name and len(named) == 1:
        return named[0]
    primary_screen = QGuiApplication.primaryScreen()
    if monitor.primary and primary_screen is not None:
        return primary_screen
    size_matches = []
    for screen in screens:
        geometry = screen.geometry()
        ratio = screen.devicePixelRatio()
        physical = round(geometry.width() * ratio), round(geometry.height() * ratio)
        if physical == (monitor.width, monitor.height):
            size_matches.append(screen)
    candidates = [screen for screen in size_matches if screen is not primary_screen] or size_matches or screens
    if len(candidates) == 1 or primary_screen is None:
        return candidates[0]
    physical_primary = next((item for item in list_monitors() if item.primary), None)
    if physical_primary is None:
        return candidates[0]
    physical_dx = (monitor.left + monitor.width / 2) - (physical_primary.left + physical_primary.width / 2)
    physical_dy = (monitor.top + monitor.height / 2) - (physical_primary.top + physical_primary.height / 2)
    qt_primary_center = primary_screen.geometry().center()

    def direction_score(screen: QScreen) -> float:
        center = screen.geometry().center()
        qt_dx = center.x() - qt_primary_center.x()
        qt_dy = center.y() - qt_primary_center.y()
        physical_length = max(1.0, abs(physical_dx) + abs(physical_dy))
        qt_length = max(1.0, abs(qt_dx) + abs(qt_dy))
        return abs(physical_dx / physical_length - qt_dx / qt_length) + abs(physical_dy / physical_length - qt_dy / qt_length)

    return min(candidates, key=direction_score)


def _qt_rectangle(monitor: Monitor, physical: tuple[int, int, int, int]) -> QRect:
    screen = _screen_for_monitor(monitor)
    geometry = screen.geometry()
    scale_x = monitor.width / geometry.width()
    scale_y = monitor.height / geometry.height()
    left, top, width, height = physical
    return QRect(
        geometry.left() + round((left - monitor.left) / scale_x),
        geometry.top() + round((top - monitor.top) / scale_y),
        max(1, round(width / scale_x)),
        max(1, round(height / scale_y)),
    )


class CaptureExcludedWidget:
    """Apply Windows capture exclusion after a native window is created."""

    _capture_exclusion_applied = False

    def _apply_capture_exclusion(self) -> None:
        if self._capture_exclusion_applied:
            return
        exclude_window_from_capture(int(self.winId()))
        self._capture_exclusion_applied = True


class RegionSelector(CaptureExcludedWidget, QDialog):
    """Select a rectangular region with four draggable corner handles."""

    MINIMUM_SIZE = 32

    def __init__(
        self,
        monitor: Monitor,
        capture: dict[str, Any],
        selection: dict[str, Any],
        initial: list[int] | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self.monitor = monitor
        self.capture = capture
        self.style = selection
        self.setWindowTitle("Select Capture Region")
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.Tool | Qt.WindowStaysOnTopHint)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setMouseTracking(True)
        self.qt_screen = _screen_for_monitor(monitor)
        screen_geometry = self.qt_screen.geometry()
        self.scale_x = monitor.width / screen_geometry.width()
        self.scale_y = monitor.height / screen_geometry.height()
        minimum = max(16, int(capture.get("region_minimum_size", self.MINIMUM_SIZE)))
        self.minimum_width = max(2, round(minimum / self.scale_x))
        self.minimum_height = max(2, round(minimum / self.scale_y))
        self.setGeometry(screen_geometry)
        if initial:
            x, y, width, height = initial
            self.selection = QRect(
                round(x / self.scale_x),
                round(y / self.scale_y),
                round(width / self.scale_x),
                round(height / self.scale_y),
            ).intersected(self.rect())
        else:
            margin_x = max(32, round(monitor.width * 0.15))
            margin_y = max(32, round(monitor.height * 0.15))
            self.selection = self.rect().adjusted(margin_x, margin_y, -margin_x, -margin_y)
        if self.selection.width() < self.minimum_width or self.selection.height() < self.minimum_height:
            self.selection = QRect(0, 0, min(round(640 / self.scale_x), self.width()), min(round(360 / self.scale_y), self.height()))
        self._drag_mode: str | None = None
        self._drag_origin = QPointF()
        self._start_selection = QRect(self.selection)
        self.selected_region: list[int] | None = None

    def showEvent(self, event) -> None:
        super().showEvent(event)
        self._apply_capture_exclusion()
        self.raise_()
        self.activateWindow()

    def _handles(self) -> dict[str, QPointF]:
        rectangle = QRectF(self.selection)
        return {
            "top_left": rectangle.topLeft(),
            "top_right": rectangle.topRight(),
            "bottom_left": rectangle.bottomLeft(),
            "bottom_right": rectangle.bottomRight(),
        }

    def _handle_at(self, point: QPointF) -> str | None:
        radius = max(8.0, float(self.style.get("handle_size", 14)))
        for name, center in self._handles().items():
            if QRectF(center.x() - radius, center.y() - radius, radius * 2, radius * 2).contains(point):
                return name
        return None

    def mousePressEvent(self, event: QMouseEvent) -> None:
        if event.button() != Qt.LeftButton:
            return
        point = event.position()
        handle = self._handle_at(point)
        if handle:
            self._drag_mode = handle
        elif self.selection.contains(point.toPoint()):
            self._drag_mode = "move"
        else:
            self._drag_mode = "bottom_right"
            self.selection = QRect(point.toPoint(), point.toPoint())
        self._drag_origin = point
        self._start_selection = QRect(self.selection)
        event.accept()

    def mouseMoveEvent(self, event: QMouseEvent) -> None:
        point = event.position()
        if self._drag_mode is None:
            if self._handle_at(point):
                self.setCursor(Qt.SizeFDiagCursor)
            elif self.selection.contains(point.toPoint()):
                self.setCursor(Qt.SizeAllCursor)
            else:
                self.unsetCursor()
            return
        delta = point - self._drag_origin
        rectangle = QRect(self._start_selection)
        if self._drag_mode == "move":
            rectangle.translate(round(delta.x()), round(delta.y()))
            if rectangle.left() < 0:
                rectangle.moveLeft(0)
            if rectangle.top() < 0:
                rectangle.moveTop(0)
            if rectangle.right() >= self.width():
                rectangle.moveRight(self.width() - 1)
            if rectangle.bottom() >= self.height():
                rectangle.moveBottom(self.height() - 1)
        else:
            position = point.toPoint()
            if "left" in self._drag_mode:
                rectangle.setLeft(min(position.x(), rectangle.right() - self.minimum_width))
            else:
                rectangle.setRight(max(position.x(), rectangle.left() + self.minimum_width))
            if "top" in self._drag_mode:
                rectangle.setTop(min(position.y(), rectangle.bottom() - self.minimum_height))
            else:
                rectangle.setBottom(max(position.y(), rectangle.top() + self.minimum_height))
            if self.capture.get("region_lock_aspect"):
                physical_ratio = float(self.capture.get("region_aspect_width", 16)) / float(self.capture.get("region_aspect_height", 9))
                logical_ratio = physical_ratio * self.scale_y / self.scale_x
                if rectangle.width() / max(1, rectangle.height()) > logical_ratio:
                    desired_height = max(self.minimum_height, round(rectangle.width() / logical_ratio))
                    if "top" in self._drag_mode:
                        rectangle.setTop(rectangle.bottom() - desired_height)
                    else:
                        rectangle.setBottom(rectangle.top() + desired_height)
                else:
                    desired_width = max(self.minimum_width, round(rectangle.height() * logical_ratio))
                    if "left" in self._drag_mode:
                        rectangle.setLeft(rectangle.right() - desired_width)
                    else:
                        rectangle.setRight(rectangle.left() + desired_width)
            rectangle = rectangle.intersected(self.rect())
        if self.capture.get("region_snap_to_edges"):
            threshold = 12
            if abs(rectangle.left()) <= threshold:
                if self._drag_mode == "move":
                    rectangle.moveLeft(0)
                else:
                    rectangle.setLeft(0)
            if abs(rectangle.top()) <= threshold:
                if self._drag_mode == "move":
                    rectangle.moveTop(0)
                else:
                    rectangle.setTop(0)
            if abs(self.width() - 1 - rectangle.right()) <= threshold:
                if self._drag_mode == "move":
                    rectangle.moveRight(self.width() - 1)
                else:
                    rectangle.setRight(self.width() - 1)
            if abs(self.height() - 1 - rectangle.bottom()) <= threshold:
                if self._drag_mode == "move":
                    rectangle.moveBottom(self.height() - 1)
                else:
                    rectangle.setBottom(self.height() - 1)
        if rectangle.width() >= self.minimum_width and rectangle.height() >= self.minimum_height:
            self.selection = rectangle.normalized()
            self.update()
        event.accept()

    def mouseReleaseEvent(self, event: QMouseEvent) -> None:
        if event.button() == Qt.LeftButton:
            self._drag_mode = None
            event.accept()

    def mouseDoubleClickEvent(self, event: QMouseEvent) -> None:
        if event.button() == Qt.LeftButton and self.selection.contains(event.position().toPoint()):
            self._accept_selection()

    def keyPressEvent(self, event) -> None:
        if event.key() in {Qt.Key_Return, Qt.Key_Enter}:
            self._accept_selection()
            return
        if event.key() == Qt.Key_Escape:
            self.reject()
            return
        super().keyPressEvent(event)

    def _accept_selection(self) -> None:
        self.selected_region = [
            round(self.selection.x() * self.scale_x),
            round(self.selection.y() * self.scale_y),
            round(self.selection.width() * self.scale_x),
            round(self.selection.height() * self.scale_y),
        ]
        self.accept()

    def paintEvent(self, _event: QPaintEvent) -> None:
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        painter.fillRect(self.rect(), _qcolor(self.style.get("dim_color", "#00000099")))
        painter.setCompositionMode(QPainter.CompositionMode_Clear)
        painter.fillRect(self.selection, Qt.transparent)
        painter.setCompositionMode(QPainter.CompositionMode_SourceOver)
        pen = QPen(_qcolor(self.style.get("line_color", "#4C97FFFF")))
        pen.setWidth(max(1, int(self.style.get("line_width", 2))))
        pen.setDashPattern([
            max(1.0, float(self.style.get("dash_length", 9))),
            max(1.0, float(self.style.get("dash_gap", 6))),
        ])
        painter.setPen(pen)
        painter.setBrush(Qt.NoBrush)
        painter.drawRect(self.selection)
        size = max(6, int(self.style.get("handle_size", 14)))
        painter.setPen(QPen(
            _qcolor(self.style.get("handle_border", "#2F70EEFF")),
            max(1, int(self.style.get("handle_border_width", 2))),
        ))
        painter.setBrush(_qcolor(self.style.get("handle_color", "#FFFFFFFF")))
        for point in self._handles().values():
            rectangle = QRectF(point.x() - size / 2, point.y() - size / 2, size, size)
            if self.style.get("handle_shape") == "square":
                painter.drawRect(rectangle)
            else:
                painter.drawEllipse(rectangle)
        if self.style.get("show_dimensions", True):
            physical_width = round(self.selection.width() * self.scale_x)
            physical_height = round(self.selection.height() * self.scale_y)
            label = f"{physical_width} × {physical_height}  •  Enter to accept  •  Esc to cancel"
            font = QFont("Segoe UI", max(8, int(self.style.get("dimension_size", 12))))
            painter.setFont(font)
            metrics = painter.fontMetrics()
            width = metrics.horizontalAdvance(label) + 20
            height = metrics.height() + 12
            x = max(8, min(self.width() - width - 8, self.selection.center().x() - width // 2))
            y = max(8, self.selection.top() - height - 10)
            if y == 8:
                y = min(self.height() - height - 8, self.selection.bottom() + 10)
            painter.setPen(Qt.NoPen)
            painter.setBrush(QColor(8, 12, 20, 220))
            painter.drawRoundedRect(QRectF(x, y, width, height), 6, 6)
            painter.setPen(_qcolor(self.style.get("dimension_color", "#FFFFFFFF")))
            painter.drawText(QRect(x, y, width, height), Qt.AlignCenter, label)


class PreviewOverlay(CaptureExcludedWidget, QWidget):
    """Show the exact post-processing caption without entering the capture."""

    def __init__(self) -> None:
        super().__init__(None)
        self._pixmap = QPixmap()
        self.setWindowFlags(
            Qt.FramelessWindowHint
            | Qt.Tool
            | Qt.WindowStaysOnTopHint
            | Qt.WindowTransparentForInput
            | Qt.WindowDoesNotAcceptFocus
        )
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setAttribute(Qt.WA_ShowWithoutActivating)

    def show_for(self, rectangle: tuple[int, int, int, int], monitor: Monitor, caption: dict[str, Any]) -> None:
        left, top, width, height = rectangle
        overlay = render_caption_overlay((width, height), caption)
        image = QImage(ImageQt(overlay)).copy()
        self._pixmap = QPixmap.fromImage(image)
        self.setGeometry(_qt_rectangle(monitor, rectangle))
        self.show()
        self._apply_capture_exclusion()
        self.raise_()
        self.update()

    def paintEvent(self, _event: QPaintEvent) -> None:
        painter = QPainter(self)
        painter.drawPixmap(self.rect(), self._pixmap)


class CountdownOverlay(CaptureExcludedWidget, QWidget):
    finished = Signal()

    def __init__(self) -> None:
        super().__init__(None)
        self.value = 0
        self.setWindowFlags(
            Qt.FramelessWindowHint
            | Qt.Tool
            | Qt.WindowStaysOnTopHint
            | Qt.WindowTransparentForInput
            | Qt.WindowDoesNotAcceptFocus
        )
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setAttribute(Qt.WA_ShowWithoutActivating)

    def show_value(self, rectangle: tuple[int, int, int, int], monitor: Monitor, value: int) -> None:
        self.value = value
        self.setGeometry(_qt_rectangle(monitor, rectangle))
        self.show()
        self._apply_capture_exclusion()
        self.raise_()
        self.update()

    def paintEvent(self, _event: QPaintEvent) -> None:
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        diameter = min(180, max(100, min(self.width(), self.height()) // 5))
        circle = QRectF((self.width() - diameter) / 2, (self.height() - diameter) / 2, diameter, diameter)
        painter.setPen(QPen(QColor(110, 161, 255, 240), 3))
        painter.setBrush(QColor(8, 12, 20, 225))
        painter.drawEllipse(circle)
        painter.setPen(QColor(255, 255, 255))
        painter.setFont(QFont("Segoe UI", diameter // 2, QFont.Bold))
        painter.drawText(circle, Qt.AlignCenter, str(self.value))
