param([string]$DotNet)

$ErrorActionPreference = 'Stop'
if (-not $DotNet) {
    $candidates = @(
        "$env:LOCALAPPDATA\ScreenDemoRecorder\dotnet\dotnet.exe",
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
        "$env:ProgramFiles\dotnet\dotnet.exe"
    )
    $DotNet = $candidates | Where-Object { Test-Path -LiteralPath (Join-Path (Split-Path $_) 'sdk\10.0.400\dotnet.dll') } | Select-Object -First 1
    if (-not $DotNet) { throw '.NET SDK 10.0.400 was not found. Run build-native.bat to install it, or pass -DotNet with its full path.' }
}
$DotNet = (Resolve-Path -LiteralPath $DotNet).Path
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget-packages'
$buildId = 'native-diagnostic-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
$output = Join-Path $PSScriptRoot "dist\$buildId"
$checks = Join-Path $PSScriptRoot "build\$buildId"
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
Push-Location $PSScriptRoot
try {
    Write-Output 'Building the local source with diagnostic logging. No Git update is performed.'
    & $DotNet build native\ScreenDemoRecorder.sln -c Release -p:RecorderDiagnostics=true --nologo --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic solution build failed.' }
    & $DotNet native\tests\ScreenDemoRecorder.CoreChecks\bin\diagnostic\Release\net10.0\ScreenDemoRecorder.CoreChecks.dll
    if ($LASTEXITCODE -ne 0) { throw 'Native core checks failed.' }
    & $DotNet publish native\src\ScreenDemoRecorder\ScreenDemoRecorder.csproj -c Release -r win-x64 --self-contained true `
        -p:RecorderDiagnostics=true -p:PublishSingleFile=false -o $output --nologo --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic publish failed.' }
    & "$PSScriptRoot\scripts\test-native-build.ps1" -Executable "$output\ScreenDemoRecorder.exe" -TestDirectory $checks -Diagnostic
    Copy-Item -LiteralPath "$PSScriptRoot\LICENSE", "$PSScriptRoot\THIRD_PARTY_NOTICES.md" -Destination $output
    Copy-Item -LiteralPath "$PSScriptRoot\docs\DIAGNOSTICS.md" -Destination $output
    $archive = "$output.zip"
    if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive" }
    Compress-Archive -Path "$output\*" -DestinationPath $archive -CompressionLevel Optimal
    Write-Output "Diagnostic executable: $output\ScreenDemoRecorder.exe"
    Write-Output "Portable archive: $archive"
    Write-Output "Test results: $checks"
}
finally { Pop-Location }
