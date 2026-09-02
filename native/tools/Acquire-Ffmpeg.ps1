[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$releaseTag = 'autobuild-2026-09-01-13-13'
$archiveName = 'ffmpeg-N-126386-gc27482a18d-win64-lgpl-shared.zip'
$checksumsHash = '20ff2b3002887ce4c69caf6a34c73d0bfdcaa8265f2f9f720464f0d32e789bd5'
$archiveHash = '4c5abe4d63748166de2c917074fcbacf52276b0cd2542ebf59b09aaa98f547f6'
$releaseRoot = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$releaseTag"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$destination = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'native\vendor\ffmpeg'))
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("screen-demo-recorder-ffmpeg-" + [Guid]::NewGuid().ToString('N'))

$existingBuild = Join-Path $destination 'BUILD.txt'
if (-not $Force -and (Test-Path -LiteralPath (Join-Path $destination 'ffmpeg.exe')) -and
    (Test-Path -LiteralPath $existingBuild)) {
    $buildText = Get-Content -LiteralPath $existingBuild -Raw
    if ($buildText.Contains("Release: $releaseTag") -and $buildText.Contains("Archive: $archiveName")) {
        Write-Output "Verified FFmpeg runtime is already installed at $destination"
        return
    }
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $checksumsPath = Join-Path $temporaryRoot 'checksums.sha256'
    $archivePath = Join-Path $temporaryRoot $archiveName
    Invoke-WebRequest -Uri "$releaseRoot/checksums.sha256" -OutFile $checksumsPath
    $actualChecksumsHash = (Get-FileHash -LiteralPath $checksumsPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualChecksumsHash -ne $checksumsHash) {
        throw "The FFmpeg checksum manifest failed verification. Expected $checksumsHash, received $actualChecksumsHash."
    }

    $checksumLine = Get-Content -LiteralPath $checksumsPath | Where-Object { $_ -match "\s+$([regex]::Escape($archiveName))$" }
    if ($checksumLine.Count -ne 1 -or $checksumLine -notmatch '^([0-9a-fA-F]{64})\s+') {
        throw "The pinned FFmpeg archive is missing from the verified checksum manifest."
    }
    $expectedArchiveHash = $Matches[1].ToLowerInvariant()
    if ($expectedArchiveHash -ne $archiveHash) {
        throw "The verified checksum manifest does not contain the pinned FFmpeg archive hash $archiveHash."
    }
    Invoke-WebRequest -Uri "$releaseRoot/$archiveName" -OutFile $archivePath
    $actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualArchiveHash -ne $expectedArchiveHash) {
        throw "The FFmpeg archive failed verification. Expected $expectedArchiveHash, received $actualArchiveHash."
    }

    $expandedPath = Join-Path $temporaryRoot 'expanded'
    Expand-Archive -LiteralPath $archivePath -DestinationPath $expandedPath
    $ffmpegExecutables = @(Get-ChildItem -LiteralPath $expandedPath -Recurse -Filter 'ffmpeg.exe')
    $license = Get-ChildItem -LiteralPath $expandedPath -Recurse -File |
        Where-Object { $_.Name -in @('LICENSE.txt', 'COPYING.LGPLv2.1') } | Select-Object -First 1
    if ($ffmpegExecutables.Count -ne 1 -or $null -eq $license) {
        throw 'The verified FFmpeg archive does not contain the expected executable and license.'
    }
    $ffmpegExecutable = $ffmpegExecutables[0]

    $resolvedDestination = [System.IO.Path]::GetFullPath($destination)
    if (-not $resolvedDestination.StartsWith($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'The FFmpeg destination resolved outside the repository.'
    }
    if (Test-Path -LiteralPath $resolvedDestination) {
        Remove-Item -LiteralPath $resolvedDestination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedDestination | Out-Null
    Copy-Item -Path (Join-Path $ffmpegExecutable.DirectoryName '*') -Destination $resolvedDestination -Force
    Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $resolvedDestination 'COPYING.LGPLv2.1.txt') -Force
    Set-Content -LiteralPath (Join-Path $resolvedDestination 'BUILD.txt') -Encoding utf8 -Value @(
        "Release: $releaseTag"
        "Archive: $archiveName"
        "SHA-256: $expectedArchiveHash"
        "Source: $releaseRoot/$archiveName"
    )
    Write-Output "Verified FFmpeg runtime installed at $resolvedDestination"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
