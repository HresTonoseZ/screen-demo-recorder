# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Composite the native Windows cursor into MSS frames when requested."""

from __future__ import annotations

import ctypes
import ctypes.wintypes

from PIL import Image


CURSOR_SHOWING = 0x00000001
DIB_RGB_COLORS = 0
BI_RGB = 0
DI_NORMAL = 0x0003


class CURSORINFO(ctypes.Structure):
    _fields_ = [
        ("cbSize", ctypes.wintypes.DWORD),
        ("flags", ctypes.wintypes.DWORD),
        ("hCursor", ctypes.wintypes.HANDLE),
        ("ptScreenPos", ctypes.wintypes.POINT),
    ]


class ICONINFO(ctypes.Structure):
    _fields_ = [
        ("fIcon", ctypes.wintypes.BOOL),
        ("xHotspot", ctypes.wintypes.DWORD),
        ("yHotspot", ctypes.wintypes.DWORD),
        ("hbmMask", ctypes.wintypes.HBITMAP),
        ("hbmColor", ctypes.wintypes.HBITMAP),
    ]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", ctypes.wintypes.DWORD),
        ("biWidth", ctypes.wintypes.LONG),
        ("biHeight", ctypes.wintypes.LONG),
        ("biPlanes", ctypes.wintypes.WORD),
        ("biBitCount", ctypes.wintypes.WORD),
        ("biCompression", ctypes.wintypes.DWORD),
        ("biSizeImage", ctypes.wintypes.DWORD),
        ("biXPelsPerMeter", ctypes.wintypes.LONG),
        ("biYPelsPerMeter", ctypes.wintypes.LONG),
        ("biClrUsed", ctypes.wintypes.DWORD),
        ("biClrImportant", ctypes.wintypes.DWORD),
    ]


class RGBQUAD(ctypes.Structure):
    _fields_ = [
        ("rgbBlue", ctypes.c_ubyte),
        ("rgbGreen", ctypes.c_ubyte),
        ("rgbRed", ctypes.c_ubyte),
        ("rgbReserved", ctypes.c_ubyte),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", RGBQUAD * 1)]


class CursorPainter:
    """Reuse one 32-bit DIB so cursor compositing does not allocate GDI objects per frame."""

    def __init__(self, width: int, height: int, left: int, top: int) -> None:
        self.width = width
        self.height = height
        self.left = left
        self.top = top
        self.byte_count = width * height * 4
        self.user32 = ctypes.windll.user32
        self.gdi32 = ctypes.windll.gdi32
        self.gdi32.CreateCompatibleDC.argtypes = [ctypes.wintypes.HDC]
        self.gdi32.CreateCompatibleDC.restype = ctypes.wintypes.HDC
        self.gdi32.CreateDIBSection.argtypes = [
            ctypes.wintypes.HDC,
            ctypes.POINTER(BITMAPINFO),
            ctypes.wintypes.UINT,
            ctypes.POINTER(ctypes.c_void_p),
            ctypes.wintypes.HANDLE,
            ctypes.wintypes.DWORD,
        ]
        self.gdi32.CreateDIBSection.restype = ctypes.wintypes.HBITMAP
        self.gdi32.SelectObject.argtypes = [ctypes.wintypes.HDC, ctypes.wintypes.HANDLE]
        self.gdi32.SelectObject.restype = ctypes.wintypes.HANDLE
        self.gdi32.DeleteObject.argtypes = [ctypes.wintypes.HANDLE]
        self.gdi32.DeleteObject.restype = ctypes.wintypes.BOOL
        self.gdi32.DeleteDC.argtypes = [ctypes.wintypes.HDC]
        self.gdi32.DeleteDC.restype = ctypes.wintypes.BOOL
        self.user32.GetCursorInfo.argtypes = [ctypes.POINTER(CURSORINFO)]
        self.user32.GetCursorInfo.restype = ctypes.wintypes.BOOL
        self.user32.GetIconInfo.argtypes = [ctypes.wintypes.HANDLE, ctypes.POINTER(ICONINFO)]
        self.user32.GetIconInfo.restype = ctypes.wintypes.BOOL
        self.user32.DrawIconEx.argtypes = [
            ctypes.wintypes.HDC,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.wintypes.HANDLE,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.wintypes.UINT,
            ctypes.wintypes.HBRUSH,
            ctypes.wintypes.UINT,
        ]
        self.user32.DrawIconEx.restype = ctypes.wintypes.BOOL
        self.dc = self.gdi32.CreateCompatibleDC(0)
        if not self.dc:
            raise ctypes.WinError()
        information = BITMAPINFO()
        information.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        information.bmiHeader.biWidth = width
        information.bmiHeader.biHeight = -height
        information.bmiHeader.biPlanes = 1
        information.bmiHeader.biBitCount = 32
        information.bmiHeader.biCompression = BI_RGB
        self.bits = ctypes.c_void_p()
        self.bitmap = self.gdi32.CreateDIBSection(
            self.dc,
            ctypes.byref(information),
            DIB_RGB_COLORS,
            ctypes.byref(self.bits),
            None,
            0,
        )
        if not self.bitmap or not self.bits.value:
            self.gdi32.DeleteDC(self.dc)
            raise ctypes.WinError()
        self.previous = self.gdi32.SelectObject(self.dc, self.bitmap)

    def composite(self, bgra: bytes) -> bytes:
        if len(bgra) != self.byte_count:
            raise ValueError("Unexpected MSS frame size")
        ctypes.memmove(self.bits, bgra, self.byte_count)
        cursor = CURSORINFO(cbSize=ctypes.sizeof(CURSORINFO))
        if self.user32.GetCursorInfo(ctypes.byref(cursor)) and cursor.flags & CURSOR_SHOWING:
            icon = ICONINFO()
            if self.user32.GetIconInfo(cursor.hCursor, ctypes.byref(icon)):
                try:
                    x = cursor.ptScreenPos.x - self.left - int(icon.xHotspot)
                    y = cursor.ptScreenPos.y - self.top - int(icon.yHotspot)
                    self.user32.DrawIconEx(self.dc, x, y, cursor.hCursor, 0, 0, 0, None, DI_NORMAL)
                finally:
                    if icon.hbmMask:
                        self.gdi32.DeleteObject(icon.hbmMask)
                    if icon.hbmColor:
                        self.gdi32.DeleteObject(icon.hbmColor)
        raw = ctypes.string_at(self.bits, self.byte_count)
        image = Image.frombuffer("RGBA", (self.width, self.height), raw, "raw", "BGRA", 0, 1)
        return image.convert("RGB").tobytes()

    def close(self) -> None:
        if self.dc:
            if self.previous:
                self.gdi32.SelectObject(self.dc, self.previous)
            if self.bitmap:
                self.gdi32.DeleteObject(self.bitmap)
            self.gdi32.DeleteDC(self.dc)
            self.previous = None
            self.bitmap = None
            self.dc = None

    def __enter__(self) -> "CursorPainter":
        return self

    def __exit__(self, *_args) -> None:
        self.close()
