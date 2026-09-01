param(
    [ValidateSet("onefile", "onedir")]
    [string]$Package = "onefile",
    [string]$Python = ""
)

$ErrorActionPreference = "Stop"
$repoPath = [System.IO.Path]::GetFullPath($PSScriptRoot)
$venvPath = [System.IO.Path]::GetFullPath((Join-Path $repoPath ".venv-build"))
$buildPath = [System.IO.Path]::GetFullPath((Join-Path $repoPath "build"))
$distPath = [System.IO.Path]::GetFullPath((Join-Path $repoPath "dist"))

foreach ($path in @($venvPath, $buildPath, $distPath)) {
    if (-not $path.StartsWith($repoPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Build path escaped the repository: $path"
    }
}

$pythonArguments = @()
if ($Python) {
    $pythonCommand = $Python
} elseif (Get-Command py -ErrorAction SilentlyContinue) {
    $pythonCommand = "py"
    $pythonArguments = @("-3")
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    $pythonCommand = "python"
} else {
    throw "Python 3.10 or newer was not found. Pass its path with -Python."
}

if (-not (Test-Path -LiteralPath $venvPath -PathType Container)) {
    & $pythonCommand @pythonArguments -m venv $venvPath
}
$venvPython = Join-Path $venvPath "Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
    throw "The build environment is incomplete: $venvPython"
}

& $venvPython -m pip install --upgrade pip
& $venvPython -m pip install -r (Join-Path $repoPath "requirements-build.txt")
& $venvPython -m unittest discover -s (Join-Path $repoPath "tests") -v

foreach ($path in @($buildPath, $distPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path | Out-Null
}

$assetsPath = Join-Path $buildPath "generated"
& $venvPython (Join-Path $repoPath "tools\generate_build_assets.py") $assetsPath
$iconPath = Join-Path $assetsPath "ScreenDemoRecorder.ico"
$versionPath = Join-Path $assetsPath "version-info.txt"
$entryPath = Join-Path $repoPath "run_screen_demo_recorder.py"
$hooksPath = Join-Path $repoPath "packaging\hooks"
$smokeDist = Join-Path $buildPath "smoke-dist"
$smokeWork = Join-Path $buildPath "smoke-work"
$specPath = Join-Path $buildPath "spec"

$commonArguments = @(
    "--noconfirm",
    "--clean",
    "--name", "ScreenDemoRecorder",
    "--icon", $iconPath,
    "--version-file", $versionPath,
    "--additional-hooks-dir", $hooksPath,
    "--hidden-import", "mss.windows",
    "--hidden-import", "pynput.keyboard._win32",
    "--collect-all", "imageio_ffmpeg",
    "--specpath", $specPath
)

$basePythonPath = (& $venvPython -c "import sys; print(sys.base_prefix)").Trim()
$isolatedPathEntries = @(
    (Split-Path -Parent $venvPython),
    $basePythonPath,
    [System.Environment]::SystemDirectory,
    $env:SystemRoot,
    (Join-Path $env:SystemRoot "System32\Wbem"),
    (Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0")
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } | Select-Object -Unique
$originalPath = $env:PATH
$originalQtPluginPath = $env:QT_PLUGIN_PATH
$originalQmlImportPath = $env:QML2_IMPORT_PATH
$env:PATH = $isolatedPathEntries -join [System.IO.Path]::PathSeparator
Remove-Item Env:QT_PLUGIN_PATH -ErrorAction SilentlyContinue
Remove-Item Env:QML2_IMPORT_PATH -ErrorAction SilentlyContinue

try {
    & $venvPython -m PyInstaller @commonArguments --console --onedir --distpath $smokeDist --workpath $smokeWork $entryPath
    $smokeExecutable = Join-Path $smokeDist "ScreenDemoRecorder\ScreenDemoRecorder.exe"
    if (-not (Test-Path -LiteralPath $smokeExecutable -PathType Leaf)) {
        throw "The diagnostic build did not produce ScreenDemoRecorder.exe"
    }
    $smoke = Start-Process -FilePath $smokeExecutable -ArgumentList "--smoke-test" -Wait -PassThru -WindowStyle Hidden
    if ($smoke.ExitCode -ne 0) {
        throw "The diagnostic executable failed its Qt startup check with exit code $($smoke.ExitCode)"
    }

    if ($Package -eq "onedir") {
        & $venvPython -m PyInstaller @commonArguments --windowed --onedir --distpath $distPath --workpath (Join-Path $buildPath "onedir-work") $entryPath
        $result = Join-Path $distPath "ScreenDemoRecorder\ScreenDemoRecorder.exe"
    } else {
        & $venvPython -m PyInstaller @commonArguments --windowed --onefile --distpath $distPath --workpath (Join-Path $buildPath "onefile-work") $entryPath
        $result = Join-Path $distPath "ScreenDemoRecorder.exe"
    }

    if (-not (Test-Path -LiteralPath $result -PathType Leaf)) {
        throw "The final build did not produce the expected executable: $result"
    }
    $finalSmoke = Start-Process -FilePath $result -ArgumentList "--smoke-test" -Wait -PassThru -WindowStyle Hidden
    if ($finalSmoke.ExitCode -ne 0) {
        throw "The final executable failed its Qt startup check with exit code $($finalSmoke.ExitCode)"
    }
} finally {
    $env:PATH = $originalPath
    if ($null -eq $originalQtPluginPath) {
        Remove-Item Env:QT_PLUGIN_PATH -ErrorAction SilentlyContinue
    } else {
        $env:QT_PLUGIN_PATH = $originalQtPluginPath
    }
    if ($null -eq $originalQmlImportPath) {
        Remove-Item Env:QML2_IMPORT_PATH -ErrorAction SilentlyContinue
    } else {
        $env:QML2_IMPORT_PATH = $originalQmlImportPath
    }
}

Write-Host "Build complete: $result"
