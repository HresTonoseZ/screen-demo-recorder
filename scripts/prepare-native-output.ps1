param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$output = [System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$outputPrefix = $output + [System.IO.Path]::DirectorySeparatorChar
$repository = [System.IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$allowed = @('screen-demo-recorder', 'screen-demo-recorder-diagnostics') | ForEach-Object {
    [System.IO.Path]::GetFullPath((Join-Path $repository "dist\$_"))
}
if ($output -notin $allowed) { throw "Refusing to clean an unexpected output directory: $output" }
foreach ($candidate in @($repository, (Join-Path $repository 'dist'), $output)) {
    if ((Test-Path -LiteralPath $candidate) -and
        ((Get-Item -LiteralPath $candidate -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to clean through a linked directory: $candidate"
    }
}
if (Test-Path -LiteralPath $output) {
    if (Get-ChildItem -LiteralPath $output -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
        throw "Refusing to clean output containing linked files or directories: $output"
    }
}

Get-Process -Name 'ScreenDemoRecorder' -ErrorAction SilentlyContinue | ForEach-Object {
    $path = $null
    try { $path = $_.Path } catch { }
    if ($path -and [System.IO.Path]::GetFullPath($path).StartsWith(
        $outputPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "Closing previous build process $($_.Id) and its encoders..."
        & taskkill.exe /PID $_.Id /T /F
        if ($LASTEXITCODE -ne 0) { throw "Could not close previous build process $($_.Id)." }
    }
}

if (Test-Path -LiteralPath $output) {
    Write-Host "Removing previous build output: $output"
    Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction Stop
}

if (Test-Path -LiteralPath $output) {
    throw "The previous build output could not be removed: $output"
}
