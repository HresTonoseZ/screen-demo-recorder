param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$TestDirectory,
    [switch]$Diagnostic,
    [switch]$ShowGifResults
)

$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$TestDirectory = [IO.Path]::GetFullPath($TestDirectory)
if (Test-Path -LiteralPath $TestDirectory) { throw 'Use a new test directory so stale results cannot pass checks.' }
New-Item -ItemType Directory -Path $TestDirectory | Out-Null

function Write-TestStatus([string]$Message) {
    $line = "$(Get-Date -Format o) $Message"
    Add-Content -LiteralPath (Join-Path $TestDirectory 'tests.log') -Value $line
    Write-Host $line
}

function Stop-TestProcess($Process) {
    if (-not $Process.HasExited) {
        # The PID belongs to the child this test started; kill its encoders as well.
        & taskkill.exe /PID $Process.Id /T /F | Out-Null
        if (-not $Process.WaitForExit(5000)) { throw "Could not terminate test process $($Process.Id)." }
    }
}

function Invoke-AppCheck([string]$Command, [string]$Name, [int]$TimeoutSeconds = 120) {
    $directory = Join-Path $TestDirectory $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    Write-TestStatus "START: $Name; command=$Command; timeout=$TimeoutSeconds seconds"
    $process = $null
    try {
        $process = Start-Process -FilePath $Executable -ArgumentList @($Command, ('"' + $directory + '"')) -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $directory 'stdout.log') -RedirectStandardError (Join-Path $directory 'stderr.log')
        # Retain the handle so Windows PowerShell can read ExitCode after redirected output closes.
        $null = $process.Handle
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $process.WaitForExit(1000)) {
            if ([DateTime]::UtcNow -gt $deadline) { throw "Test $Name timed out; inspect $directory" }
        }
        if ($process.ExitCode -ne 0) { throw "Test $Name failed with exit code $($process.ExitCode); inspect $directory" }
        Write-TestStatus "PASS: $Name; exit=0"
    }
    catch { Write-TestStatus "FAIL: $Name; $_"; throw }
    finally { if ($process) { Stop-TestProcess $process; $process.Dispose() } }
}

Invoke-AppCheck '--build-flavor-check' 'flavor'
$expectedFlavor = if ($Diagnostic) { 'DIAGNOSTIC' } else { 'NORMAL' }
if ((Get-Content -LiteralPath "$TestDirectory\flavor\flavor.txt" -Raw) -ne $expectedFlavor) { throw 'Unexpected compiled build flavor.' }
Invoke-AppCheck '--smoke-test' 'ui'
Invoke-AppCheck '--cpu-pipeline-smoke-test' 'pipeline'
Get-Content -LiteralPath "$TestDirectory\ui\result.txt", "$TestDirectory\pipeline\result.txt"
$gifResult = Get-Content -LiteralPath "$TestDirectory\pipeline\gif-result.txt" -Raw
if ($Diagnostic -or $ShowGifResults) { Write-Output $gifResult }

if ($Diagnostic) {
    Invoke-AppCheck '--diagnostic-log-self-test' 'logging' 90
    $logs = Get-ChildItem -LiteralPath "$TestDirectory\logging\diagnostics" -Filter '*.log'
    if (-not ($logs | Get-Content | Select-String -SimpleMatch 'SELF_TEST PASS:')) { throw 'The diagnostic self-test did not persist success.' }
    foreach ($stage in @('D3D.GetTextureDescription', 'D3D.CreateStagingTexture', 'D3D.CopySubresourceRegion', 'D3D.Map')) {
        if (-not ($logs | Get-Content | Select-String -SimpleMatch "READBACK_STAGE_TEST PASS: $stage")) {
            throw "Native readback stage was not verified: $stage"
        }
    }
    $pipelineLogs = Get-ChildItem -LiteralPath "$TestDirectory\pipeline\diagnostics" -Filter '*.log' | Get-Content
    foreach ($stage in @('D3D.GetTextureDescription', 'D3D.CreateStagingTexture', 'D3D.CopySubresourceRegion', 'D3D.Map', 'D3D.CopyMappedRows', 'D3D.Unmap')) {
        if (-not ($pipelineLogs | Select-String -Pattern ("END #\d+ " + [regex]::Escape($stage) + ';'))) {
            throw "Real pipeline did not complete the instrumented stage: $stage"
        }
    }
    Write-Output 'PASS: precise native-call diagnostics in delayed-stage and real recording tests.'
    $forceDirectory = Join-Path $TestDirectory 'forced-stop'
    New-Item -ItemType Directory -Path $forceDirectory | Out-Null
    Write-TestStatus 'START: forced-stop; command=--diagnostic-force-stop-test; timeout=25 seconds; intentional termination expected'
    $process = $null
    try {
        $process = Start-Process -FilePath $Executable -ArgumentList @('--diagnostic-force-stop-test', ('"' + $forceDirectory + '"')) -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $forceDirectory 'stdout.log') -RedirectStandardError (Join-Path $forceDirectory 'stderr.log')
        $null = $process.Handle
        $deadline = [DateTime]::UtcNow.AddSeconds(25)
        do {
            if ($process.WaitForExit(250)) { throw 'The forced-stop test exited before the parent terminated it.' }
            $content = Get-ChildItem "$forceDirectory\diagnostics\*.log" -ErrorAction SilentlyContinue | Get-Content -Raw
            if ([DateTime]::UtcNow -gt $deadline) { throw 'The watchdog did not persist the simulated UI stall.' }
        } until ($content -match 'UI_UNRESPONSIVE' -and $content -match 'SELF_TEST forced-stop UI stall')
        Stop-TestProcess $process
        $persisted = Get-ChildItem "$forceDirectory\diagnostics\*.log" | Get-Content -Raw
        if ($persisted -notmatch 'UI_UNRESPONSIVE' -or $persisted -match 'PROCESS_EXIT') { throw 'Forced-stop log persistence check failed.' }
        Write-TestStatus 'PASS: forced-stop; diagnostic log survived forced termination.'
    }
    catch { Write-TestStatus "FAIL: forced-stop; $_"; throw }
    finally { if ($process) { Stop-TestProcess $process; $process.Dispose() } }
}
elseif (Get-ChildItem -LiteralPath $TestDirectory -Filter 'diagnostics' -Directory -Recurse) {
    throw 'The normal build unexpectedly created diagnostic logs.'
}
