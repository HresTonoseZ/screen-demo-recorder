# SPDX-FileCopyrightText: 2026 HresTonoseZ
#
# SPDX-License-Identifier: MIT

import ast
import re
import unittest
from pathlib import Path

from screen_demo_recorder import __version__
from screen_demo_recorder.app import parse_args


ROOT = Path(__file__).resolve().parents[1]


class ProjectTests(unittest.TestCase):
    def test_every_python_module_parses(self):
        for path in sorted(ROOT.rglob("*.py")):
            if any(part in {"build", "dist", ".venv-build"} for part in path.parts):
                continue
            with self.subTest(path=path.relative_to(ROOT)):
                ast.parse(path.read_text(encoding="utf-8"), filename=str(path))

    def test_version_matches_metadata_and_readme(self):
        pyproject = (ROOT / "pyproject.toml").read_text(encoding="utf-8")
        readme = (ROOT / "README.md").read_text(encoding="utf-8")
        self.assertIn(f'version = "{__version__}"', pyproject)
        self.assertIn(f"Version: `{__version__}`", readme)

    def test_active_project_has_no_retired_product_identity(self):
        retired = re.compile(r"blender", re.IGNORECASE)
        paths = [ROOT / "README.md", ROOT / "pyproject.toml", *sorted((ROOT / "screen_demo_recorder").glob("*.py"))]
        for path in paths:
            with self.subTest(path=path.relative_to(ROOT)):
                self.assertIsNone(retired.search(path.read_text(encoding="utf-8")))

    def test_build_and_archive_rules_are_present(self):
        self.assertTrue((ROOT / "build.ps1").is_file())
        build_script = (ROOT / "build.ps1").read_text(encoding="utf-8")
        self.assertIn("$isolatedPathEntries", build_script)
        self.assertIn('"--smoke-test"', build_script)
        self.assertIn("--console --onedir", build_script)
        self.assertIn("--windowed --onefile", build_script)
        ignore = (ROOT / ".gitignore").read_text(encoding="utf-8").splitlines()
        self.assertIn("*.zip", ignore)
        self.assertIn("*.rar", ignore)

    def test_smoke_test_argument_is_available_for_packaged_runtime_checks(self):
        self.assertTrue(parse_args(["--smoke-test"]).smoke_test)

    def test_native_build_uses_the_vendored_ffmpeg_runtime(self):
        runtime = ROOT / "native" / "vendor" / "ffmpeg"
        required = {
            "ffmpeg.exe", "ffprobe.exe", "avcodec-63.dll", "avdevice-63.dll",
            "avfilter-12.dll", "avformat-63.dll", "avutil-61.dll",
            "swresample-7.dll", "swscale-10.dll", "BUILD.txt",
            "COPYING.LGPLv2.1.txt",
        }
        self.assertEqual(required, {path.name for path in runtime.iterdir() if path.name != "ffplay.exe"})
        self.assertFalse((ROOT / "native" / "tools" / "Acquire-Ffmpeg.ps1").exists())
        project = (ROOT / "native" / "src" / "ScreenDemoRecorder" / "ScreenDemoRecorder.csproj").read_text(encoding="utf-8")
        self.assertIn("ValidateVendoredFfmpeg", project)
        self.assertNotIn("Invoke-WebRequest", project)
        self.assertNotIn("Acquire-Ffmpeg", project)


if __name__ == "__main__":
    unittest.main()
