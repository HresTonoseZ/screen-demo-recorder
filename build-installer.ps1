param(
    [string]$DotNet,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$VerifyRecording,
    [switch]$BenchmarkStartup,
    [switch]$VerifyPackage
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
    & (Join-Path $PSScriptRoot 'build-native.ps1') -DotNet $DotNet -Configuration $Configuration `
        -VerifyRecording:$VerifyRecording -BenchmarkStartup:$BenchmarkStartup

    [xml]$project = Get-Content -LiteralPath 'native\src\ScreenDemoRecorder\ScreenDemoRecorder.csproj'
    $productVersion = [string]$project.Project.PropertyGroup.Version
    if ($productVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid native product version: $productVersion" }
    $publishPath = (Resolve-Path -LiteralPath 'dist\screen-demo-recorder').Path
    $payloadPath = Join-Path $PSScriptRoot ('build\native-installer-payload-' + [Guid]::NewGuid().ToString('N') + '\Payload.wxs')
    & (Join-Path $PSScriptRoot 'tools\generate_installer_payload.ps1') -PublishDirectory $publishPath -OutputPath $payloadPath
    [xml]$payload = Get-Content -LiteralPath $payloadPath -Raw
    $payloadCount = $payload.SelectNodes("//*[local-name()='Component']").Count
    $expectedCount = (Get-ChildItem -LiteralPath $publishPath -Recurse -File | Where-Object Extension -ne '.pdb').Count
    if ($payloadCount -ne $expectedCount) { throw "Installer payload contains $payloadCount components; expected $expectedCount." }
    $installerPath = Join-Path $PSScriptRoot 'dist\installer'
    & $DotNet build 'native\installer\ScreenDemoRecorder.Installer.wixproj' -c $Configuration --nologo --disable-build-servers `
        "-p:ProductVersion=$productVersion" "-p:GeneratedPayload=$payloadPath" "-p:OutputPath=$installerPath"
    if ($LASTEXITCODE -ne 0) { throw 'Native installer build failed.' }

    $msi = Get-ChildItem -LiteralPath $installerPath -Filter "ScreenDemoRecorder-$productVersion-x64.msi" -Recurse -File |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $msi) { throw 'The native installer MSI was not produced.' }
    & (Join-Path $PSScriptRoot 'tools\check_installer.ps1') -MsiPath $msi.FullName `
        -PublishDirectory $publishPath -ExpectedVersion $productVersion
    if ($VerifyPackage) {
        $administrativePath = Join-Path $PSScriptRoot `
            ('build\native-installer-extract-' + [Guid]::NewGuid().ToString('N'))
        & (Join-Path $PSScriptRoot 'tools\check_installer_payload.ps1') -MsiPath $msi.FullName `
            -PublishDirectory $publishPath -OutputDirectory $administrativePath
    }
    Write-Output "Native installer: $($msi.FullName)"
}
finally { Pop-Location }
