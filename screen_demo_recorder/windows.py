# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Windows display discovery and capture-exclusion helpers."""

from __future__ import annotations

import ctypes
import ctypes.wintypes
import sys
from dataclasses import dataclass


WDA_NONE = 0x00000000
WDA_EXCLUDEFROMCAPTURE = 0x00000011
MINIMUM_WINDOWS_BUILD = 19041


@dataclass(frozen=True)
class Monitor:
    index: int
    left: int
    top: int
    width: int
    height: int
    display_name: str = ""
    device_name: str = ""
    primary: bool = False

    @property
    def right(self) -> int:
        return self.left + self.width

    @property
    def bottom(self) -> int:
        return self.top + self.height

    @property
    def label(self) -> str:
        location = f"{self.left:+d}, {self.top:+d}"
        name = self.display_name or f"Monitor {self.index}"
        primary = " — Primary" if self.primary else ""
        return f"{name}{primary} — {self.width} × {self.height} ({location})"

    def absolute_region(self, relative: list[int] | None) -> tuple[int, int, int, int]:
        if relative is None:
            return self.left, self.top, self.width, self.height
        x, y, width, height = relative
        x = max(0, min(int(x), self.width - 16))
        y = max(0, min(int(y), self.height - 16))
        width = max(16, min(int(width), self.width - x))
        height = max(16, min(int(height), self.height - y))
        return self.left + x, self.top + y, width, height


def require_supported_windows() -> None:
    if sys.platform != "win32":
        raise RuntimeError("Screen Demo Recorder supports Windows only")
    build = int(sys.getwindowsversion().build)
    if build < MINIMUM_WINDOWS_BUILD:
        raise RuntimeError("Windows 10 version 2004 or newer is required")


def enable_pixel_accurate_coordinates() -> None:
    """Enable per-monitor-v2 DPI awareness before creating UI windows."""

    if sys.platform != "win32":
        return
    try:
        user32 = ctypes.windll.user32
        user32.SetProcessDpiAwarenessContext.argtypes = [ctypes.c_void_p]
        user32.SetProcessDpiAwarenessContext.restype = ctypes.wintypes.BOOL
        if not user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4)):
            error = ctypes.get_last_error()
            if error not in {0, 5}:
                raise ctypes.WinError(error)
    except (AttributeError, OSError):
        ctypes.windll.user32.SetProcessDPIAware()


def list_monitors() -> list[Monitor]:
    """Return physical Win32 monitor rectangles and display names."""

    require_supported_windows()
    user32 = ctypes.windll.user32

    class MONITORINFOEXW(ctypes.Structure):
        _fields_ = [
            ("cbSize", ctypes.wintypes.DWORD),
            ("rcMonitor", ctypes.wintypes.RECT),
            ("rcWork", ctypes.wintypes.RECT),
            ("dwFlags", ctypes.wintypes.DWORD),
            ("szDevice", ctypes.wintypes.WCHAR * 32),
        ]

    class DISPLAY_DEVICEW(ctypes.Structure):
        _fields_ = [
            ("cb", ctypes.wintypes.DWORD),
            ("DeviceName", ctypes.wintypes.WCHAR * 32),
            ("DeviceString", ctypes.wintypes.WCHAR * 128),
            ("StateFlags", ctypes.wintypes.DWORD),
            ("DeviceID", ctypes.wintypes.WCHAR * 128),
            ("DeviceKey", ctypes.wintypes.WCHAR * 128),
        ]

    discovered: list[tuple[int, int, int, int, str, str, bool]] = []
    callback_type = ctypes.WINFUNCTYPE(
        ctypes.wintypes.BOOL,
        ctypes.wintypes.HMONITOR,
        ctypes.wintypes.HDC,
        ctypes.POINTER(ctypes.wintypes.RECT),
        ctypes.wintypes.LPARAM,
    )
    user32.GetMonitorInfoW.argtypes = [ctypes.wintypes.HMONITOR, ctypes.POINTER(MONITORINFOEXW)]
    user32.GetMonitorInfoW.restype = ctypes.wintypes.BOOL
    user32.EnumDisplayDevicesW.argtypes = [
        ctypes.wintypes.LPCWSTR,
        ctypes.wintypes.DWORD,
        ctypes.POINTER(DISPLAY_DEVICEW),
        ctypes.wintypes.DWORD,
    ]
    user32.EnumDisplayDevicesW.restype = ctypes.wintypes.BOOL
    user32.EnumDisplayMonitors.argtypes = [
        ctypes.wintypes.HDC,
        ctypes.POINTER(ctypes.wintypes.RECT),
        callback_type,
        ctypes.wintypes.LPARAM,
    ]
    user32.EnumDisplayMonitors.restype = ctypes.wintypes.BOOL

    def visit(handle, _dc, _rect, _data):
        information = MONITORINFOEXW(cbSize=ctypes.sizeof(MONITORINFOEXW))
        if not user32.GetMonitorInfoW(handle, ctypes.byref(information)):
            return True
        device = DISPLAY_DEVICEW(cb=ctypes.sizeof(DISPLAY_DEVICEW))
        display_name = ""
        if user32.EnumDisplayDevicesW(information.szDevice, 0, ctypes.byref(device), 0):
            display_name = device.DeviceString.strip()
        rect = information.rcMonitor
        discovered.append(
            (
                rect.left,
                rect.top,
                rect.right - rect.left,
                rect.bottom - rect.top,
                display_name,
                information.szDevice,
                bool(information.dwFlags & 1),
            )
        )
        return True

    callback = callback_type(visit)
    if not user32.EnumDisplayMonitors(0, None, callback, 0):
        raise ctypes.WinError()
    discovered.sort(key=lambda item: (not item[6], item[1], item[0]))
    return [Monitor(index, *item) for index, item in enumerate(discovered, start=1)]


def exclude_window_from_capture(handle: int) -> None:
    """Exclude one current-process top-level window from public capture APIs."""

    require_supported_windows()
    user32 = ctypes.windll.user32
    user32.SetWindowDisplayAffinity.argtypes = [ctypes.wintypes.HWND, ctypes.wintypes.DWORD]
    user32.SetWindowDisplayAffinity.restype = ctypes.wintypes.BOOL
    user32.GetWindowDisplayAffinity.argtypes = [ctypes.wintypes.HWND, ctypes.POINTER(ctypes.wintypes.DWORD)]
    user32.GetWindowDisplayAffinity.restype = ctypes.wintypes.BOOL
    ctypes.set_last_error(0)
    if not user32.SetWindowDisplayAffinity(ctypes.wintypes.HWND(int(handle)), WDA_EXCLUDEFROMCAPTURE):
        raise ctypes.WinError(ctypes.get_last_error())
    affinity = ctypes.wintypes.DWORD(WDA_NONE)
    if not user32.GetWindowDisplayAffinity(ctypes.wintypes.HWND(int(handle)), ctypes.byref(affinity)):
        raise ctypes.WinError(ctypes.get_last_error())
    if affinity.value != WDA_EXCLUDEFROMCAPTURE:
        raise RuntimeError("Windows did not apply capture exclusion to an application window")
