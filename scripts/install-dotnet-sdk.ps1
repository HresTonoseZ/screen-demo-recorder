param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$InstallDir
)

$ErrorActionPreference = 'Stop'
$installerUri = 'https://dot.net/v1/dotnet-install.ps1'
$operationId = [Guid]::NewGuid().ToString('N')
$installerPath = Join-Path $env:TEMP "screen-demo-recorder-dotnet-install-$operationId.ps1"
$stdoutPath = Join-Path $env:TEMP "screen-demo-recorder-dotnet-install-$operationId.stdout.log"
$stderrPath = Join-Path $env:TEMP "screen-demo-recorder-dotnet-install-$operationId.stderr.log"

function Download-FileWithProgress([string]$Uri, [string]$Destination)
{
    Add-Type -AssemblyName System.Net.Http
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $client = [Net.Http.HttpClient]::new()
    try
    {
        $response = $client.GetAsync($Uri, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $response.EnsureSuccessStatusCode()
        $total = $response.Content.Headers.ContentLength
        $inputStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try
        {
            $buffer = New-Object byte[] 65536
            [long]$received = 0
            while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0)
            {
                $outputStream.Write($buffer, 0, $read)
                $received += $read
                if ($total -gt 0)
                {
                    $percent = [Math]::Min(100, [Math]::Round($received * 100 / $total))
                    $status = '{0:N1} KB of {1:N1} KB' -f ($received / 1KB), ($total / 1KB)
                    Write-Progress -Activity 'Downloading the official Microsoft installer' -Status $status -PercentComplete $percent
                }
                else
                {
                    Write-Progress -Activity 'Downloading the official Microsoft installer' -Status ('{0:N1} KB' -f ($received / 1KB)) -PercentComplete -1
                }
            }
        }
        finally
        {
            $outputStream.Dispose()
            $inputStream.Dispose()
            $response.Dispose()
            Write-Progress -Activity 'Downloading the official Microsoft installer' -Completed
        }
    }
    finally
    {
        $client.Dispose()
    }
}

try
{
    Download-FileWithProgress $installerUri $installerPath

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"' + $installerPath + '"'),
        '-Version', ('"' + $Version + '"'),
        '-InstallDir', ('"' + $InstallDir + '"'),
        '-Architecture', 'x64',
        '-NoPath'
    )
    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not $process.WaitForExit(250))
    {
        $seconds = $timer.Elapsed.TotalSeconds
        $percent = [Math]::Min(95, 5 + [Math]::Floor(90 * (1 - [Math]::Exp(-$seconds / 35))))
        Write-Progress -Activity ".NET SDK $Version installation" `
            -Status ("Downloading and extracting... elapsed {0:mm\:ss}" -f $timer.Elapsed) `
            -PercentComplete $percent
    }
    $process.WaitForExit()
    $process.Refresh()
    Write-Progress -Activity ".NET SDK $Version installation" -Status 'Completed' -PercentComplete 100
    Write-Progress -Activity ".NET SDK $Version installation" -Completed

    if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath }
    if (Test-Path -LiteralPath $stderrPath)
    {
        $errors = Get-Content -LiteralPath $stderrPath
        if ($errors) { $errors | ForEach-Object { Write-Host $_ -ForegroundColor Yellow } }
    }
    exit $process.ExitCode
}
catch
{
    Write-Error $_
    exit 1
}
finally
{
    Remove-Item -LiteralPath $installerPath, $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
}
