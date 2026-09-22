# Internal helpers shared by the two explicit, per-user registration scripts.
Set-StrictMode -Version Latest

$script:RegistrationOwner = 'ToolKeeper.MarkPad.Portable.v1'
$script:MarkdownProgId = 'ToolKeeper.MarkPad.Markdown'
$script:ProtocolProgId = 'ToolKeeper.MarkPad.Protocol'
$script:CapabilitiesPath = 'Software\ToolKeeper\MarkPad\Capabilities'
$script:ProtocolPath = 'Software\Classes\toolkeeper-markpad'
$script:OwnedRoots = @(
    "Software\Classes\$script:MarkdownProgId",
    "Software\Classes\$script:ProtocolProgId",
    $script:CapabilitiesPath,
    $script:ProtocolPath
)

function Test-MarkPadOwnedKey([string]$Path) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Path, $false)
    if ($null -eq $key) { return $false }
    try { return $key.GetValue('ToolKeeperRegistrationOwner') -eq $script:RegistrationOwner }
    finally { $key.Dispose() }
}

function Assert-MarkPadRegistrationAvailable {
    foreach ($path in $script:OwnedRoots) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path, $false)
        if ($null -eq $key) { continue }
        try {
            if ($key.GetValue('ToolKeeperRegistrationOwner') -ne $script:RegistrationOwner) {
                throw "An existing registration owns HKCU\$path. Remove it through its installer before registering this portable build."
            }
        }
        finally { $key.Dispose() }
    }
    $applications = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\RegisteredApplications', $false)
    if ($null -ne $applications) {
        try {
            $existing = $applications.GetValue('MarkPad')
            if ($null -ne $existing -and $existing -ne $script:CapabilitiesPath) {
                throw 'Another application already registered the name MarkPad. Its registration has been preserved.'
            }
        }
        finally { $applications.Dispose() }
    }
}

function Set-MarkPadRegistryValues([string]$Path, [hashtable]$Values) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($Path, $true)
    try {
        foreach ($name in $Values.Keys) {
            $key.SetValue([string]$name, [string]$Values[$name], [Microsoft.Win32.RegistryValueKind]::String)
        }
    }
    finally { $key.Dispose() }
}

function Send-MarkPadAssociationChanged {
    try {
        if (-not ('MarkPadRegistration.NativeMethods' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace MarkPadRegistration {
    public static class NativeMethods {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
'@
        }
        [MarkPadRegistration.NativeMethods]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    catch { Write-Warning 'Registration changed; sign out and back in if Explorer does not refresh its Open With list.' }
}

