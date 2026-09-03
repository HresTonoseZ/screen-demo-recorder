param([Parameter(Mandatory = $true)][string]$ReportDirectory)

$ErrorActionPreference = 'Stop'
$report = (Resolve-Path -LiteralPath $ReportDirectory).Path
function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$summary = Get-Content -LiteralPath (Join-Path $report 'summary.txt') -Raw
Require ($summary -match 'PASS: diagnostic build and all automatic tests completed\.') 'Build summary is not successful.'
$console = Get-Content -LiteralPath (Join-Path $report 'build.log') -Raw
Require ($console -match 'Automatic tests WILL RUN' -and $console -match 'START: compile' -and $console -match 'PASS: forced-stop') 'Build console is missing stage or test output.'
foreach ($stage in @('sdk-info', 'compile', 'core-tests', 'publish')) {
    Require (Test-Path -LiteralPath (Join-Path $report "$stage.stdout.log")) "Missing stdout for $stage."
    Require (Test-Path -LiteralPath (Join-Path $report "$stage.stderr.log")) "Missing stderr for $stage."
    Require ($summary -match "PASS: $stage") "Missing successful stage: $stage."
}
$testLog = Get-Content -LiteralPath (Join-Path $report 'tests\tests.log') -Raw
foreach ($test in @('flavor', 'ui', 'pipeline', 'logging', 'forced-stop')) {
    Require ($testLog -match "START: $test;" -and $testLog -match "PASS: $test;") "Test start/result missing: $test."
    foreach ($stream in @('stdout', 'stderr')) {
        Require (Test-Path -LiteralPath (Join-Path $report "tests\$test\$stream.log")) "Missing $test $stream."
    }
    Require (@(Get-ChildItem -LiteralPath (Join-Path $report "tests\$test\diagnostics") -Filter '*.log').Count -gt 0) "Missing diagnostic log: $test."
}
foreach ($extension in @('mp4', 'gif', 'mkv')) {
    Require (@(Get-ChildItem -LiteralPath (Join-Path $report 'tests\pipeline') -Filter "*.$extension").Count -gt 0) "Missing test recordings: $extension."
}
Require ((Get-Content -LiteralPath (Join-Path $report 'tests\flavor\flavor.txt') -Raw) -eq 'DIAGNOSTIC') 'Wrong application flavor.'
Write-Output 'PASS: complete diagnostic report includes commands, compiler output, all test stages, logs and recordings.'
