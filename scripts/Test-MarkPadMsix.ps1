#Requires -Version 5.1
<#
.SYNOPSIS
Checks Hanqing (汗青) MSIX payload and identity without installing the package.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$IdentityFile
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$identity = Get-Content -LiteralPath $IdentityFile -Raw -Encoding UTF8 | ConvertFrom-Json
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
try {
    $entries = @{}
    foreach ($entry in $archive.Entries) { $entries[$entry.FullName.Replace('\', '/')] = $entry }
    foreach ($name in @('AppxManifest.xml', 'AppxBlockMap.xml', '[Content_Types].xml', 'resources.pri',
        'Hanqing.exe', 'Hanqing.dll', 'Hanqing.runtimeconfig.json', 'Hanqing.deps.json', 'ToolKeeper.UI.dll', 'coreclr.dll', 'PresentationFramework.dll',
        'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'WebView2Loader.dll', 'THIRD-PARTY-NOTICES.md',
        'WinRT.Runtime.dll', 'Microsoft.Windows.SDK.NET.dll', 'System.Security.Cryptography.ProtectedData.dll',
        'Assets/StoreLogo.png', 'Assets/Square44x44Logo.png', 'Assets/Square150x150Logo.png')) {
        if (-not $entries.ContainsKey($name)) { throw "Missing required package file: $name" }
    }
    if (@($entries.Keys | Where-Object { $_ -like 'licenses/*' }).Count -eq 0) { throw 'Third-party license texts are missing.' }
    foreach ($name in $entries.Keys) {
        if ($name -match '(?i)^MarkPad\.(exe|dll|runtimeconfig\.json|deps\.json)$') { throw "Stale application filename in package: $name" }
        if ($name -match '^resources\..+\.pri$') { throw "Unexpected split resource index in single-package MSIX: $name" }
        if ($name -match '(?i)(^|/)(Register-MarkPad|Unregister-MarkPad|Registration.Common)\.ps1$|\.pfx$|\.pdb$|README-PORTABLE\.txt$|StoreIdentity.*\.json$') {
            throw "Unexpected development/portable file in package: $name"
        }
    }
    $reader = New-Object IO.StreamReader($entries['AppxManifest.xml'].Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifest.Package.Identity.Name -cne $identity.Name -or $manifest.Package.Identity.Publisher -cne $identity.Publisher -or
        $manifest.Package.Properties.PublisherDisplayName -cne $identity.PublisherDisplayName) { throw 'MSIX identity does not exactly match the supplied Partner Center values.' }
    $namespace = New-Object Xml.XmlNamespaceManager($manifest.NameTable)
    $namespace.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespace.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
    $namespace.AddNamespace('uap3', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/3')
    $namespace.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')
    $app = $manifest.SelectSingleNode('/f:Package/f:Applications/f:Application', $namespace)
    if ($app.Executable -ne 'Hanqing.exe' -or $app.EntryPoint -ne 'Windows.FullTrustApplication') { throw 'Unexpected desktop entry point.' }
    $visuals = $app.SelectSingleNode('uap:VisualElements', $namespace)
    if ($manifest.Package.Properties.DisplayName -cne '汗青 - Markdown Writer' -or
        $visuals.DisplayName -cne '汗青 - Markdown Writer') { throw 'Unexpected application display name.' }
    $association = $app.SelectSingleNode('f:Extensions/uap:Extension/uap3:FileTypeAssociation', $namespace)
    if ($null -eq $association -or $association.Parameters -ne '"%1"' -or $association.MultiSelectModel -ne 'Document' -or
        $association.SelectSingleNode('uap:SupportedFileTypes/uap:FileType', $namespace).InnerText -ne '.md') { throw 'Markdown activation declaration is missing or incorrect.' }
    $protocol = $app.SelectSingleNode('f:Extensions/uap3:Extension/uap3:Protocol', $namespace)
    if ($null -eq $protocol -or $protocol.Name -ne 'toolkeeper-markpad' -or $protocol.Parameters -ne '"%1"') { throw 'Protocol activation declaration is missing or incorrect.' }
    $capability = $manifest.SelectSingleNode('/f:Package/f:Capabilities/rescap:Capability[@Name="runFullTrust"]', $namespace)
    if ($null -eq $capability) { throw 'Desktop runFullTrust declaration is missing.' }
    Write-Output "MSIX identity, activation declarations and self-contained payload verified: $PackagePath"
}
finally { $archive.Dispose() }
