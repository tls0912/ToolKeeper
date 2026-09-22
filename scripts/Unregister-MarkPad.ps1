#Requires -Version 5.1
<#
.SYNOPSIS
Removes only MarkPad portable registrations created by Register-MarkPad.ps1.
.DESCRIPTION
Preserves documents, app data, other applications, .md defaults, and UserChoice.
.EXAMPLE
.\Unregister-MarkPad.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Windows is required for app registration.' }
. (Join-Path $PSScriptRoot 'Registration.Common.ps1')

$owned = @($script:OwnedRoots | Where-Object { Test-MarkPadOwnedKey $_ })
if ($owned.Count -eq 0) {
    Write-Output 'No MarkPad portable registration created by these scripts was found.'
    return
}
if ($PSCmdlet.ShouldProcess('Owned MarkPad portable registration in HKCU', 'Remove .md Open With entry, app capabilities, and URI registration')) {
    if ($owned -contains "Software\Classes\$script:MarkdownProgId") {
        $openWith = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\.md\OpenWithProgids', $true)
        if ($null -ne $openWith) {
            try { $openWith.DeleteValue($script:MarkdownProgId, $false) }
            finally { $openWith.Dispose() }
        }
    }
    if ($owned -contains $script:CapabilitiesPath) {
        $applications = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\RegisteredApplications', $true)
        if ($null -ne $applications) {
            try {
                if ($applications.GetValue('MarkPad') -eq $script:CapabilitiesPath) {
                    $applications.DeleteValue('MarkPad', $false)
                }
            }
            finally { $applications.Dispose() }
        }
    }
    foreach ($path in $owned) {
        # Each path is a fixed MarkPad subtree whose ownership marker was verified above.
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($path, $false)
    }
    Send-MarkPadAssociationChanged
    Write-Output 'Removed MarkPad portable registration. Documents and local app data were preserved.'
}

