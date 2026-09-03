param(
    [string]$DotNet,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$VerifyRecording,
    [switch]$BenchmarkStartup
)

$ErrorActionPreference = 'Stop'
if (-not $DotNet) {
    $userSdk = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $userSdk) { $DotNet = $userSdk }
    else { $DotNet = (Get-Command dotnet -ErrorAction Stop).Source }
}
$DotNet = (Resolve-Path -LiteralPath $DotNet).Path
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget-packages'
Push-Location $PSScriptRoot
try {
    # A unique test output keeps a failed check from locking the next verification build.
    $verificationId = 'verification-' + [Guid]::NewGuid().ToString('N')
    $verificationBase = "bin\$verificationId\"
    & $DotNet build native\ScreenDemoRecorder.sln -c $Configuration -p:RecorderDiagnostics=false --nologo --disable-build-servers `
        "-p:BaseOutputPath=$verificationBase"
    if ($LASTEXITCODE -ne 0) { throw 'Native solution build failed.' }
    & $DotNet "native\tests\ScreenDemoRecorder.CoreChecks\$verificationBase$Configuration\net10.0\ScreenDemoRecorder.CoreChecks.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Native core checks failed.' }
    & $DotNet publish native\src\ScreenDemoRecorder\ScreenDemoRecorder.csproj -c $Configuration -r win-x64 --self-contained true -p:RecorderDiagnostics=false -p:PublishSingleFile=false -o dist\native-preview --nologo --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Native publishing failed.' }
    $exe = Join-Path $PSScriptRoot 'dist\native-preview\ScreenDemoRecorder.exe'
    $checkPath = Join-Path $PSScriptRoot ('build\native-checks-' + [Guid]::NewGuid().ToString('N'))
    & "$PSScriptRoot\scripts\test-native-build.ps1" -Executable $exe -TestDirectory $checkPath -ShowGifResults:$VerifyRecording
    if ($BenchmarkStartup) {
        $benchmarkPath = Join-Path $PSScriptRoot ('build\native-startup-' + [Guid]::NewGuid().ToString('N'))
        $samples = foreach ($sampleIndex in 1..5) {
            $samplePath = Join-Path $benchmarkPath ("sample-$sampleIndex")
            $launchedTimestamp = [Diagnostics.Stopwatch]::GetTimestamp()
            $probe = Start-Process -FilePath $exe -ArgumentList @('--startup-probe', ('"' + $samplePath + '"')) -WindowStyle Hidden -PassThru -Wait
            if ($probe.ExitCode -ne 0) { throw "Native startup probe failed. See $samplePath\failure.txt" }
            $measurement = Get-Content -LiteralPath (Join-Path $samplePath 'result.json') -Raw | ConvertFrom-Json
            [pscustomobject]@{
                Sample = $sampleIndex
                ProcessStartToContentRenderedMilliseconds =
                    [math]::Round(($measurement.RenderedTimestamp - $launchedTimestamp) * 1000 / $measurement.StopwatchFrequency, 2)
                OnStartupToContentRenderedMilliseconds = [math]::Round($measurement.OnStartupToContentRenderedMilliseconds, 2)
            }
        }
        $warmValues = $samples[1..4].ProcessStartToContentRenderedMilliseconds | Sort-Object
        $startupReport = [pscustomobject]@{
            Definition = 'Fresh process to first fully rendered main window; the first sample follows publish, later samples may benefit from the OS file cache.'
            Samples = $samples
            FirstProcessMilliseconds = $samples[0].ProcessStartToContentRenderedMilliseconds
            WarmMedianProcessMilliseconds = [math]::Round(($warmValues[1] + $warmValues[2]) / 2, 2)
        }
        $startupReport | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $benchmarkPath 'result.json') -Encoding utf8
        Write-Output "Startup benchmark: first $($startupReport.FirstProcessMilliseconds) ms; warm median $($startupReport.WarmMedianProcessMilliseconds) ms."
        Write-Output "Startup report: $benchmarkPath\result.json"
    }
    Write-Output "Native preview: $exe"
}
finally { Pop-Location }
