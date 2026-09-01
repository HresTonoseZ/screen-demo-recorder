param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$output = [System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$outputPrefix = $output + [System.IO.Path]::DirectorySeparatorChar

Get-Process -Name 'ScreenDemoRecorder' -ErrorAction SilentlyContinue | ForEach-Object {
    $path = $null
    try { $path = $_.Path } catch { }
    if ($path -and [System.IO.Path]::GetFullPath($path).StartsWith(
        $outputPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "Closing previous preview process $($_.Id)..."
        Stop-Process -Id $_.Id -Force -ErrorAction Stop
    }
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction Stop
}

if (Test-Path -LiteralPath $output) {
    throw "The previous build output could not be removed: $output"
}
