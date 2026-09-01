param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$administrativeRoot = [IO.Path]::GetFullPath($OutputDirectory)
$logPath = "$administrativeRoot.log"
[IO.Directory]::CreateDirectory($administrativeRoot) | Out-Null

$arguments = @(
    '/a', ('"' + $msi + '"'),
    '/qn',
    '/norestart',
    ('TARGETDIR="' + $administrativeRoot + '"'),
    '/l*v', ('"' + $logPath + '"'))
$process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments `
    -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Administrative MSI extraction failed with exit code $($process.ExitCode). Log: $logPath"
}

# Administrative images preserve the LocalAppDataFolder hierarchy below TARGETDIR.
$extractedRoot = Join-Path $administrativeRoot 'LocalApp\Programs\Screen Demo Recorder'
if (-not (Test-Path -LiteralPath $extractedRoot -PathType Container)) {
    throw "Administrative image has an unexpected layout under $administrativeRoot."
}

$sourceFiles = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File |
    Where-Object Extension -ne '.pdb')
$extractedFiles = @(Get-ChildItem -LiteralPath $extractedRoot -Recurse -File)
if ($sourceFiles.Count -ne $extractedFiles.Count) {
    throw "Administrative image contains $($extractedFiles.Count) payload files; expected $($sourceFiles.Count)."
}

$mismatches = [Collections.Generic.List[string]]::new()
foreach ($source in $sourceFiles) {
    $relativePath = [IO.Path]::GetRelativePath($publishRoot, $source.FullName)
    $extractedPath = Join-Path $extractedRoot $relativePath
    if (-not (Test-Path -LiteralPath $extractedPath -PathType Leaf)) {
        $mismatches.Add("missing: $relativePath")
        continue
    }
    $sourceHash = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
    $extractedHash = (Get-FileHash -LiteralPath $extractedPath -Algorithm SHA256).Hash
    if ($sourceHash -ne $extractedHash) { $mismatches.Add("hash: $relativePath") }
}
if ($mismatches.Count -gt 0) {
    $mismatches | Select-Object -First 20 | Write-Output
    throw "$($mismatches.Count) extracted files differ from the publish payload."
}

Write-Output "PASS: administrative extraction reproduced all $($sourceFiles.Count) payload files byte-for-byte."
Write-Output "Administrative image: $extractedRoot"
Write-Output "Windows Installer log: $logPath"
