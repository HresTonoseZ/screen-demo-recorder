# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

"""Application entry point."""

from __future__ import annotations

import argparse
import logging
import sys
from pathlib import Path

from . import __version__
from .settings import SettingsStore, app_data_directory
from .windows import enable_pixel_accurate_coordinates, require_supported_windows


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Record Windows screens and build captioned animated GIFs")
    parser.add_argument("--settings", help="Use an alternate settings JSON file")
    parser.add_argument("--smoke-test", action="store_true", help=argparse.SUPPRESS)
    parser.add_argument("--version", action="version", version=__version__)
    return parser.parse_args(argv)


def _configure_logging() -> Path:
    directory = app_data_directory() / "logs"
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / "screen-demo-recorder.log"
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
        handlers=[logging.FileHandler(path, encoding="utf-8")],
        force=True,
    )
    return path


def main(argv=None) -> int:
    args = parse_args(argv)
    require_supported_windows()
    enable_pixel_accurate_coordinates()
    log_path = _configure_logging()

    from PySide6.QtCore import QLockFile
    from PySide6.QtWidgets import QApplication, QMessageBox

    from .ui import MainWindow

    application = QApplication(sys.argv[:1])
    application.setApplicationName("Screen Demo Recorder")
    application.setApplicationDisplayName("Screen Demo Recorder")
    application.setApplicationVersion(__version__)
    application.setOrganizationName("HresTonoseZ")
    if args.smoke_test:
        return 0

    lock_path = app_data_directory() / "screen-demo-recorder.lock"
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    lock = QLockFile(str(lock_path))
    lock.setStaleLockTime(0)
    if not lock.tryLock(0):
        QMessageBox.information(None, "Screen Demo Recorder", "Screen Demo Recorder is already running.")
        return 2

    def report_unhandled(error_type, error, traceback) -> None:
        logging.getLogger(__name__).exception("Unhandled application error", exc_info=(error_type, error, traceback))
        QMessageBox.critical(None, "Unexpected Error", f"{error}\n\nDetails were written to:\n{log_path}")

    sys.excepthook = report_unhandled
    try:
        store = SettingsStore(args.settings)
        window = MainWindow(store)
        window.show()
        return application.exec()
    except Exception as error:
        logging.getLogger(__name__).exception("Application startup failed")
        QMessageBox.critical(None, "Cannot Start Screen Demo Recorder", f"{error}\n\nDetails were written to:\n{log_path}")
        return 1
    finally:
        lock.unlock()


if __name__ == "__main__":
    raise SystemExit(main())
