#Requires -Version 5.1
<#
.SYNOPSIS
Adds a portable MarkPad build to the current user's Open With list and registers its URI.
.DESCRIPTION
Writes only HKCU. Does not change .md defaults, UserChoice, or other users' settings.
An existing registration created by this script can be updated to a new executable path.
.EXAMPLE
.\Register-MarkPad.ps1 -ExecutablePath 'C:\Apps\MarkPad\MarkPad.exe' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Windows is required for app registration.' }
. (Join-Path $PSScriptRoot 'Registration.Common.ps1')

$executable = Get-Item -LiteralPath $ExecutablePath -ErrorAction Stop
if ($executable.PSIsContainer -or $executable.Extension -ine '.exe') { throw 'ExecutablePath must point to the published MarkPad.exe.' }
$fullPath = $executable.FullName
if ($fullPath.Contains('"')) { throw 'The executable path contains an invalid quote.' }
Assert-MarkPadRegistrationAvailable

if ($PSCmdlet.ShouldProcess("HKCU app registration for $fullPath", 'Register .md Open With, app capabilities, and toolkeeper-markpad: URI')) {
    $command = '"{0}" "%1"' -f $fullPath
    $icon = '"{0}",0' -f $fullPath
    Set-MarkPadRegistryValues "Software\Classes\$script:MarkdownProgId" @{
        '' = 'Markdown Document'; 'FriendlyTypeName' = 'Markdown Document';
        'ToolKeeperRegistrationOwner' = $script:RegistrationOwner
    }
    Set-MarkPadRegistryValues "Software\Classes\$script:MarkdownProgId\DefaultIcon" @{ '' = $icon }
    Set-MarkPadRegistryValues "Software\Classes\$script:MarkdownProgId\shell\open\command" @{ '' = $command }

    foreach ($root in @("Software\Classes\$script:ProtocolProgId", $script:ProtocolPath)) {
        Set-MarkPadRegistryValues $root @{
            '' = 'URL:MarkPad Protocol'; 'URL Protocol' = '';
            'ToolKeeperRegistrationOwner' = $script:RegistrationOwner
        }
        Set-MarkPadRegistryValues "$root\DefaultIcon" @{ '' = $icon }
        Set-MarkPadRegistryValues "$root\shell\open\command" @{ '' = $command }
    }

    Set-MarkPadRegistryValues $script:CapabilitiesPath @{
        'ApplicationName' = 'MarkPad'; 'ApplicationDescription' = 'Offline Markdown reader and editor';
        'ApplicationIcon' = $icon; 'ToolKeeperRegistrationOwner' = $script:RegistrationOwner
    }
    Set-MarkPadRegistryValues "$script:CapabilitiesPath\FileAssociations" @{ '.md' = $script:MarkdownProgId }
    Set-MarkPadRegistryValues "$script:CapabilitiesPath\UrlAssociations" @{ 'toolkeeper-markpad' = $script:ProtocolProgId }
    Set-MarkPadRegistryValues 'Software\RegisteredApplications' @{ 'MarkPad' = $script:CapabilitiesPath }

    $openWith = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Classes\.md\OpenWithProgids', $true)
    try { $openWith.SetValue($script:MarkdownProgId, [byte[]]@(), [Microsoft.Win32.RegistryValueKind]::None) }
    finally { $openWith.Dispose() }
    Send-MarkPadAssociationChanged
    Write-Output 'Registered MarkPad for this user. Choose a default app in Windows Settings if desired.'
}

