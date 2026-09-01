param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$expectedFiles = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File |
    Where-Object Extension -ne '.pdb')
$expectedDirectories = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -Directory)

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember(
    'OpenDatabase',
    [Reflection.BindingFlags]::InvokeMethod,
    $null,
    $installer,
    [object[]]@($msi, 0))

function Get-ColumnValues([string]$sql, [int]$column = 1) {
    $view = $database.GetType().InvokeMember(
        'OpenView',
        [Reflection.BindingFlags]::InvokeMethod,
        $null,
        $database,
        [object[]]@($sql))
    try {
        $view.GetType().InvokeMember(
            'Execute',
            [Reflection.BindingFlags]::InvokeMethod,
            $null,
            $view,
            $null) | Out-Null
        $values = [Collections.Generic.List[string]]::new()
        while ($true) {
            $record = $view.GetType().InvokeMember(
                'Fetch',
                [Reflection.BindingFlags]::InvokeMethod,
                $null,
                $view,
                $null)
            if ($null -eq $record) { break }
            try {
                $value = $record.GetType().InvokeMember(
                    'StringData',
                    [Reflection.BindingFlags]::GetProperty,
                    $null,
                    $record,
                    [object[]]@($column))
                $values.Add([string]$value)
            }
            finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($record) | Out-Null }
        }
        return $values.ToArray()
    }
    finally {
        try {
            $view.GetType().InvokeMember(
                'Close',
                [Reflection.BindingFlags]::InvokeMethod,
                $null,
                $view,
                $null) | Out-Null
        }
        finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null }
    }
}

function Get-TableCount([string]$table) {
    return @(Get-ColumnValues "SELECT * FROM ``$table``").Count
}

function Get-PropertyValue([string]$name) {
    $values = @(Get-ColumnValues "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$name'")
    if ($values.Count -eq 0) { return $null }
    return $values[0]
}

try {
    $productName = Get-PropertyValue 'ProductName'
    $productVersion = Get-PropertyValue 'ProductVersion'
    $allUsers = Get-PropertyValue 'ALLUSERS'
    $fileNames = @(Get-ColumnValues "SELECT ``FileName`` FROM ``File``")
    $fileSizes = @(Get-ColumnValues "SELECT ``FileSize`` FROM ``File``")
    $cabinet = @(Get-ColumnValues "SELECT ``Cabinet`` FROM ``Media``")[0]
    $fileCount = Get-TableCount 'File'
    $componentCount = Get-TableCount 'Component'
    $registryCount = Get-TableCount 'Registry'
    $removeFolderCount = Get-TableCount 'RemoveFile'
    $shortcutCount = Get-TableCount 'Shortcut'
    $upgradeCount = Get-TableCount 'Upgrade'
    $expectedBytes = ($expectedFiles | Measure-Object Length -Sum).Sum
    $packagedBytes = ($fileSizes | ForEach-Object { [long]$_ } | Measure-Object -Sum).Sum
    $longFileNames = @($fileNames | ForEach-Object { ($_ -split '\|')[-1] })

    if ($productName -ne 'Screen Demo Recorder') { throw "Unexpected ProductName: $productName" }
    if ($productVersion -ne $ExpectedVersion) { throw "Unexpected ProductVersion: $productVersion" }
    if ($allUsers) { throw "ALLUSERS must be absent for the fixed per-user package; found '$allUsers'." }
    if ($fileCount -ne $expectedFiles.Count) { throw "MSI contains $fileCount files; expected $($expectedFiles.Count)." }
    if ($packagedBytes -ne $expectedBytes) { throw "MSI file bytes total $packagedBytes; expected $expectedBytes." }
    if ($componentCount -ne ($expectedFiles.Count + 2)) { throw "MSI contains $componentCount components; expected $($expectedFiles.Count + 2)." }
    if ($registryCount -ne ($expectedFiles.Count + 2)) { throw "MSI contains $registryCount registry rows; expected $($expectedFiles.Count + 2)." }
    if ($removeFolderCount -ne ($expectedDirectories.Count + 3)) { throw "MSI contains $removeFolderCount folder-removal rows; expected $($expectedDirectories.Count + 3)." }
    if ($shortcutCount -ne 1) { throw "MSI contains $shortcutCount shortcuts; expected 1." }
    if ($upgradeCount -lt 1) { throw 'MSI has no major-upgrade rules.' }
    if (-not $cabinet.StartsWith('#')) { throw "MSI cabinet is not embedded: $cabinet" }
    if ($longFileNames -notcontains 'ScreenDemoRecorder.exe') { throw 'MSI does not contain ScreenDemoRecorder.exe.' }
    if ($longFileNames | Where-Object { $_.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) }) {
        throw 'MSI unexpectedly contains debug symbol files.'
    }

    $msiItem = Get-Item -LiteralPath $msi
    $sha256 = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash
    Write-Output "PASS: fixed per-user scope, embedded CAB, upgrade rules, Start Menu shortcut and complete payload tables."
    Write-Output "MSI: $($msiItem.FullName)"
    Write-Output "Payload: $fileCount files, $componentCount components, $removeFolderCount folder cleanup rows."
    Write-Output "Package: $([math]::Round($msiItem.Length / 1MB, 2)) MiB, SHA-256 $sha256"
}
finally {
    [Runtime.InteropServices.Marshal]::ReleaseComObject($database) | Out-Null
    [Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null
}
