param([string]$DotNet)

$ErrorActionPreference = 'Stop'
$runId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$report = Join-Path $PSScriptRoot "diagnostic-reports\$runId"
$output = Join-Path $PSScriptRoot 'dist\screen-demo-recorder-diagnostics'
New-Item -ItemType Directory -Path $report -ErrorAction Stop | Out-Null
$summary = Join-Path $report 'summary.txt'
'RUNNING: diagnostic build and automatic tests' | Set-Content -LiteralPath $summary
$transcribing = $false
$stage = 'Initialize'

function Invoke-BuildStep([string]$Name, [string[]]$CommandArguments) {
    $script:stage = $Name
    $message = "START: $Name - $DotNet $($CommandArguments -join ' ')"
    Write-Host $message
    Add-Content -LiteralPath $summary -Value $message
    $process = Start-Process -FilePath $DotNet -ArgumentList $CommandArguments -WindowStyle Hidden -PassThru -Wait `
        -RedirectStandardOutput (Join-Path $report "$Name.stdout.log") `
        -RedirectStandardError (Join-Path $report "$Name.stderr.log")
    try {
        $process.WaitForExit()
        Get-Content -LiteralPath (Join-Path $report "$Name.stdout.log"), (Join-Path $report "$Name.stderr.log") -Encoding UTF8 | Out-Host
        if ($process.ExitCode -ne 0) { throw "$Name failed with exit code $($process.ExitCode)." }
        "PASS: $Name" | Add-Content -LiteralPath $summary
        Write-Host "PASS: $Name"
    }
    finally {
        if (-not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Host }
        $process.Dispose()
    }
}

Push-Location $PSScriptRoot
try {
    Start-Transcript -LiteralPath (Join-Path $report 'build.log') | Out-Null
    $transcribing = $true
    Write-Host 'Diagnostic build: local source only; no Git update.'
    Write-Host "Automatic tests WILL RUN after compilation. Send this entire report folder: $report"
    Write-Host "Output will be replaced: $output"
    Write-Host "Windows: $([Environment]::OSVersion); PowerShell: $($PSVersionTable.PSVersion)"
    if (Get-Command git -ErrorAction SilentlyContinue) {
        git rev-parse HEAD | Out-Host
        git status --short | Out-Host
    }
    $stage = 'SDK discovery'
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
    $env:DOTNET_CLI_UI_LANGUAGE = 'en'
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget-packages'
    Invoke-BuildStep 'sdk-info' @('--info')
    $stage = 'Clean previous build'
    & "$PSScriptRoot\scripts\prepare-native-output.ps1" -OutputDirectory $output
    Invoke-BuildStep 'compile' @('build', 'native\ScreenDemoRecorder.sln', '-c', 'Release', '-p:RecorderDiagnostics=true', '--nologo', '--disable-build-servers')
    Invoke-BuildStep 'core-tests' @('native\tests\ScreenDemoRecorder.CoreChecks\bin\diagnostic\Release\net10.0\ScreenDemoRecorder.CoreChecks.dll')
    Invoke-BuildStep 'publish' @('publish', 'native\src\ScreenDemoRecorder\ScreenDemoRecorder.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:RecorderDiagnostics=true', '-p:PublishSingleFile=false', '-o', ('"' + $output + '"'), '--nologo', '--disable-build-servers')
    $stage = 'Automatic application tests'
    Write-Host 'START: automatic UI, MP4/GIF and diagnostic hang tests. Temporary test windows and simulated stalls are expected.'
    & "$PSScriptRoot\scripts\test-native-build.ps1" -Executable "$output\ScreenDemoRecorder.exe" -TestDirectory (Join-Path $report 'tests') -Diagnostic | Out-Host
    $stage = 'Documentation'
    Copy-Item -LiteralPath "$PSScriptRoot\LICENSE", "$PSScriptRoot\THIRD_PARTY_NOTICES.md", "$PSScriptRoot\docs\DIAGNOSTICS.md" -Destination $output
    'PASS: diagnostic build and all automatic tests completed.' | Add-Content -LiteralPath $summary
    Write-Host 'PASS: diagnostic build and all automatic tests completed.'
    Write-Host "Diagnostic executable: $output\ScreenDemoRecorder.exe"
}
catch {
    $failure = "FAIL: $stage`r`n$($_ | Out-String)`r`n$($_.ScriptStackTrace)"
    Add-Content -LiteralPath $summary -Value $failure
    Write-Host $failure
    throw
}
finally {
    Write-Host "Send this entire report folder: $report"
    if ($transcribing) { Stop-Transcript | Out-Null }
    Pop-Location
}
