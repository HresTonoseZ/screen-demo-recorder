param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$destination = [IO.Path]::GetFullPath($OutputPath)
$destinationDirectory = [IO.Path]::GetDirectoryName($destination)
[IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null

function Get-StableIdentity([string]$relativePath) {
    $normalized = $relativePath.Replace('/', '\').ToLowerInvariant()
    $bytes = [Text.Encoding]::UTF8.GetBytes($normalized)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    [byte[]]$guidBytes = $hash[0..15]
    $guidBytes[7] = ($guidBytes[7] -band 0x0F) -bor 0x50
    $guidBytes[8] = ($guidBytes[8] -band 0x3F) -bor 0x80
    return [pscustomobject]@{
        Id = [Convert]::ToHexString($hash[0..11])
        Guid = [Guid]::new($guidBytes).ToString().ToUpperInvariant()
    }
}

$files = Get-ChildItem -LiteralPath $publishRoot -Recurse -File |
    Where-Object Extension -ne '.pdb' |
    Sort-Object { [IO.Path]::GetRelativePath($publishRoot, $_.FullName) }
if ($files.Count -eq 0) { throw "No installer payload files were found under $publishRoot." }
$directoriesWithRemoval = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

$settings = [Xml.XmlWriterSettings]::new()
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$settings.NewLineChars = "`n"
$writer = [Xml.XmlWriter]::Create($destination, $settings)
try {
    $writer.WriteStartDocument()
    $writer.WriteStartElement('Wix', 'http://wixtoolset.org/schemas/v4/wxs')
    $writer.WriteStartElement('Fragment')
    $writer.WriteStartElement('ComponentGroup')
    $writer.WriteAttributeString('Id', 'ApplicationFiles')
    $writer.WriteAttributeString('Directory', 'INSTALLFOLDER')
    foreach ($file in $files) {
        $relativePath = [IO.Path]::GetRelativePath($publishRoot, $file.FullName)
        $relativeDirectory = [IO.Path]::GetDirectoryName($relativePath)
        $identity = Get-StableIdentity $relativePath
        $writer.WriteStartElement('Component')
        $writer.WriteAttributeString('Id', "Payload_$($identity.Id)")
        $writer.WriteAttributeString('Guid', $identity.Guid)
        if ($relativeDirectory) { $writer.WriteAttributeString('Subdirectory', $relativeDirectory) }
        $writer.WriteStartElement('File')
        $writer.WriteAttributeString('Id', "File_$($identity.Id)")
        $writer.WriteAttributeString('Source', $file.FullName)
        $writer.WriteAttributeString('KeyPath', 'no')
        $writer.WriteEndElement()
        $writer.WriteStartElement('RegistryValue')
        $writer.WriteAttributeString('Root', 'HKCU')
        $writer.WriteAttributeString('Key', 'Software\Screen Demo Recorder\InstalledFiles')
        $writer.WriteAttributeString('Name', $identity.Id)
        $writer.WriteAttributeString('Type', 'integer')
        $writer.WriteAttributeString('Value', '1')
        $writer.WriteAttributeString('KeyPath', 'yes')
        $writer.WriteEndElement()
        if ($directoriesWithRemoval.Add([string]$relativeDirectory)) {
            $writer.WriteStartElement('RemoveFolder')
            $writer.WriteAttributeString('Id', "RemoveFolder_$($identity.Id)")
            $writer.WriteAttributeString('On', 'uninstall')
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndDocument()
}
finally { $writer.Dispose() }

Write-Output "Generated $($files.Count) per-user installer components: $destination"
