# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Desktop interface for hotkey-controlled Blender window recording."""

from __future__ import annotations

import tempfile
import threading
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from pynput import keyboard

from .recording import (
    BlenderWindow,
    WindowRecorder,
    enable_pixel_accurate_coordinates,
    list_blender_windows,
    validate_slug,
)
from .video import video_to_gif


class RecorderApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("Blender Add-on Demo Recorder")
        self.root.geometry("760x520")
        self.root.minsize(680, 480)
        self.windows: list[BlenderWindow] = []
        self.recorder = WindowRecorder()
        self.hotkey_listener = None
        self.busy = False
        self.active_conversion = None
        example = Path(__file__).resolve().parents[1] / "examples" / "demo-config.json"
        self.window_value = tk.StringVar()
        self.config_value = tk.StringVar(value=str(example))
        self.slug_value = tk.StringVar(value="blender-demo")
        self.title_value = tk.StringVar(value="Blender Demo")
        self.subtitle_value = tk.StringVar()
        self.badge_value = tk.StringVar(value="BLENDER")
        self.hotkey_value = tk.StringVar(value="<ctrl>+<shift>+<f9>")
        self.record_fps_value = tk.StringVar(value="30")
        self.gif_fps_value = tk.StringVar(value="12")
        self.status_value = tk.StringVar(value="Ready")
        self._draw()
        self.refresh_windows()
        self.apply_hotkey()
        self.root.protocol("WM_DELETE_WINDOW", self.close)

    def _draw(self) -> None:
        frame = ttk.Frame(self.root, padding=18)
        frame.pack(fill="both", expand=True)
        frame.columnconfigure(1, weight=1)
        row = 0

        ttk.Label(frame, text="Blender window").grid(row=row, column=0, sticky="w", pady=6)
        self.window_combo = ttk.Combobox(frame, textvariable=self.window_value, state="readonly")
        self.window_combo.grid(row=row, column=1, sticky="ew", padx=10)
        ttk.Button(frame, text="Refresh", command=self.refresh_windows).grid(row=row, column=2)
        row += 1

        ttk.Label(frame, text="Project config").grid(row=row, column=0, sticky="w", pady=6)
        ttk.Entry(frame, textvariable=self.config_value).grid(row=row, column=1, sticky="ew", padx=10)
        ttk.Button(frame, text="Browse", command=self.browse_config).grid(row=row, column=2)
        row += 1

        for label, variable in (
            ("Demo slug", self.slug_value),
            ("Caption title", self.title_value),
            ("Caption subtitle", self.subtitle_value),
            ("Caption badge", self.badge_value),
        ):
            ttk.Label(frame, text=label).grid(row=row, column=0, sticky="w", pady=6)
            ttk.Entry(frame, textvariable=variable).grid(row=row, column=1, columnspan=2, sticky="ew", padx=10)
            row += 1

        ttk.Separator(frame).grid(row=row, column=0, columnspan=3, sticky="ew", pady=12)
        row += 1

        ttk.Label(frame, text="Toggle hotkey").grid(row=row, column=0, sticky="w", pady=6)
        ttk.Entry(frame, textvariable=self.hotkey_value).grid(row=row, column=1, sticky="ew", padx=10)
        ttk.Button(frame, text="Apply", command=self.apply_hotkey).grid(row=row, column=2)
        row += 1

        rates = ttk.Frame(frame)
        rates.grid(row=row, column=0, columnspan=3, sticky="ew", pady=6)
        ttk.Label(rates, text="Recording FPS").pack(side="left")
        ttk.Entry(rates, textvariable=self.record_fps_value, width=8).pack(side="left", padx=(8, 24))
        ttk.Label(rates, text="GIF FPS").pack(side="left")
        ttk.Entry(rates, textvariable=self.gif_fps_value, width=8).pack(side="left", padx=8)
        row += 1

        self.toggle_button = ttk.Button(frame, text="Start Recording", command=self.toggle_recording)
        self.toggle_button.grid(row=row, column=0, columnspan=3, sticky="ew", pady=(18, 10), ipady=8)
        row += 1
        ttk.Label(frame, textvariable=self.status_value, anchor="center").grid(row=row, column=0, columnspan=3, sticky="ew")
        row += 1
        ttk.Label(
            frame,
            text="Press the configured hotkey once to start and again to stop. The MP4 and GIF are saved automatically.",
            anchor="center",
            foreground="#666666",
        ).grid(row=row, column=0, columnspan=3, sticky="ew", pady=(8, 0))

    def browse_config(self) -> None:
        path = filedialog.askopenfilename(filetypes=(("JSON config", "*.json"), ("All files", "*.*")))
        if path:
            self.config_value.set(path)

    def refresh_windows(self) -> None:
        self.windows = list_blender_windows()
        labels = [window.label for window in self.windows]
        self.window_combo["values"] = labels
        if labels:
            self.window_combo.current(0)
            self.status_value.set(f"Found {len(labels)} Blender window(s)")
        else:
            self.window_value.set("")
            self.status_value.set("Open Blender, then click Refresh")

    def apply_hotkey(self) -> None:
        value = self.hotkey_value.get().strip()
        try:
            listener = keyboard.GlobalHotKeys({value: lambda: self.root.after(0, self.toggle_recording)})
            listener.start()
        except Exception as error:
            messagebox.showerror("Invalid hotkey", str(error))
            return
        if self.hotkey_listener:
            self.hotkey_listener.stop()
        self.hotkey_listener = listener
        self.status_value.set(f"Hotkey active: {value}")

    def _selected_window(self) -> BlenderWindow:
        index = self.window_combo.current()
        if index < 0 or index >= len(self.windows):
            raise RuntimeError("Select an open Blender window")
        return self.windows[index]

    def toggle_recording(self) -> None:
        if self.busy:
            return
        if self.recorder.is_recording:
            self._stop_recording()
        else:
            self._start_recording()

    def _start_recording(self) -> None:
        try:
            window = self._selected_window()
            config = Path(self.config_value.get()).resolve()
            if not config.is_file():
                raise FileNotFoundError(f"Config not found: {config}")
            slug = validate_slug(self.slug_value.get())
            record_fps = float(self.record_fps_value.get())
            gif_fps = float(self.gif_fps_value.get())
            temporary = Path(tempfile.gettempdir()) / f"blender-demo-{slug}.mp4"
            if temporary.exists():
                temporary.unlink()
            self.recorder.start(window.handle, temporary, record_fps)
            self.active_conversion = (
                str(config),
                slug,
                self.title_value.get(),
                self.subtitle_value.get(),
                self.badge_value.get(),
                gif_fps,
            )
        except Exception as error:
            messagebox.showerror("Cannot start recording", str(error))
            return
        self.toggle_button.configure(text="Stop Recording")
        self.status_value.set(f"Recording: {window.title}")

    def _stop_recording(self) -> None:
        self.busy = True
        self.toggle_button.configure(state="disabled", text="Creating GIF...")
        self.status_value.set("Stopping recording and creating GIF...")
        conversion = self.active_conversion
        if conversion is None:
            self.busy = False
            self.toggle_button.configure(state="normal", text="Start Recording")
            messagebox.showerror("Recording failed", "Recording settings are unavailable")
            return
        threading.Thread(
            target=self._finish_recording,
            args=conversion,
            daemon=True,
        ).start()

    def _finish_recording(
        self,
        config: str,
        slug: str,
        title: str,
        subtitle: str,
        badge: str,
        gif_fps: float,
    ) -> None:
        temporary = None
        try:
            temporary = self.recorder.stop()
            archived, output, count = video_to_gif(
                config,
                temporary,
                slug,
                title=title,
                subtitle=subtitle,
                badge=badge,
                fps=gif_fps,
            )
            message = f"Saved {count} frames\nVideo: {archived}\nGIF: {output}"
            self.root.after(0, self._finish_success, message)
        except Exception as error:
            self.root.after(0, self._finish_error, str(error))
        finally:
            if temporary and temporary.is_file():
                temporary.unlink()

    def _finish_success(self, message: str) -> None:
        self.busy = False
        self.active_conversion = None
        self.toggle_button.configure(state="normal", text="Start Recording")
        self.status_value.set(message.replace("\n", " | "))
        messagebox.showinfo("Recording complete", message)

    def _finish_error(self, message: str) -> None:
        self.busy = False
        self.active_conversion = None
        self.toggle_button.configure(state="normal", text="Start Recording")
        self.status_value.set("Recording failed")
        messagebox.showerror("Recording failed", message)

    def close(self) -> None:
        if self.recorder.is_recording:
            if not messagebox.askyesno("Recording active", "Stop the active recording and close?"):
                return
            self.recorder.cancel()
        if self.hotkey_listener:
            self.hotkey_listener.stop()
        self.root.destroy()


def main() -> None:
    enable_pixel_accurate_coordinates()
    root = tk.Tk()
    RecorderApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
