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

function Stop-TestProcess($Process) {
    if (-not $Process.HasExited) {
        # The PID belongs to the child this test started; kill its encoders as well.
        & taskkill.exe /PID $Process.Id /T /F | Out-Null
        if (-not $Process.WaitForExit(5000)) { throw "Could not terminate test process $($Process.Id)." }
    }
}

function Invoke-AppCheck([string]$Command, [string]$Name, [int]$TimeoutSeconds = 120) {
    $directory = Join-Path $TestDirectory $Name
    $process = Start-Process -FilePath $Executable -ArgumentList @($Command, ('"' + $directory + '"')) -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $process.WaitForExit(1000)) {
            if ([DateTime]::UtcNow -gt $deadline) { throw "Test $Name timed out; inspect $directory" }
        }
        if ($process.ExitCode -ne 0) { throw "Test $Name failed with exit code $($process.ExitCode); inspect $directory" }
        Write-Output "PASS: $Name"
    }
    finally { Stop-TestProcess $process; $process.Dispose() }
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
    Invoke-AppCheck '--diagnostic-log-self-test' 'logging' 30
    $logs = Get-ChildItem -LiteralPath "$TestDirectory\logging\diagnostics" -Filter '*.log'
    if (-not ($logs | Get-Content | Select-String -SimpleMatch 'SELF_TEST PASS:')) { throw 'The diagnostic self-test did not persist success.' }
    $forceDirectory = Join-Path $TestDirectory 'forced-stop'
    $process = Start-Process -FilePath $Executable -ArgumentList @('--diagnostic-force-stop-test', ('"' + $forceDirectory + '"')) -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(25)
        do {
            if ($process.WaitForExit(250)) { throw 'The forced-stop test exited before the parent terminated it.' }
            $content = Get-ChildItem "$forceDirectory\diagnostics\*.log" -ErrorAction SilentlyContinue | Get-Content -Raw
            if ([DateTime]::UtcNow -gt $deadline) { throw 'The watchdog did not persist the simulated UI stall.' }
        } until ($content -match 'UI_UNRESPONSIVE' -and $content -match 'SELF_TEST forced-stop UI stall')
        Stop-TestProcess $process
        $persisted = Get-ChildItem "$forceDirectory\diagnostics\*.log" | Get-Content -Raw
        if ($persisted -notmatch 'UI_UNRESPONSIVE' -or $persisted -match 'PROCESS_EXIT') { throw 'Forced-stop log persistence check failed.' }
        Write-Output 'PASS: diagnostic log survived forced termination.'
    }
    finally { Stop-TestProcess $process; $process.Dispose() }
}
elseif (Get-ChildItem -LiteralPath $TestDirectory -Filter 'diagnostics' -Directory -Recurse) {
    throw 'The normal build unexpectedly created diagnostic logs.'
}
