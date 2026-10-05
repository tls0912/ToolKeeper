#Requires -Version 5.1
[CmdletBinding()]
param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'

try {
    $transLampRepo = Split-Path $PSScriptRoot -Parent
    $transLampProject = Join-Path $transLampRepo 'src\TransLamp\TransLamp.csproj'
    $transLampVersion = [string](([xml](Get-Content -LiteralPath $transLampProject -Raw)).Project.PropertyGroup.Version | Select-Object -First 1)
    if ($transLampVersion -notmatch '\A\d+\.\d+\.\d+(?:\.\d+)?\z') {
        throw 'TransLamp project version is invalid. Check src\TransLamp\TransLamp.csproj.'
    }

    $transLampKit = Join-Path $transLampRepo "artifacts\TransLamp-OfflineKit-$transLampVersion"
    foreach ($transLampRequired in @('TransLamp.exe', 'TransLamp.dll', 'TransLamp.runtimeconfig.json',
        'Runtime\python.exe', 'Runtime\translate.py',
        'LanguagePacks\TransLamp.LanguagePack.en-zh.1.0.0.tlpack',
        'LanguagePacks\TransLamp.LanguagePack.zh-en.1.0.0.tlpack')) {
        if (-not (Test-Path -LiteralPath (Join-Path $transLampKit $transLampRequired) -PathType Leaf)) {
            throw "TransLamp $transLampVersion Offline Kit is not ready. From the project directory, run .\scripts\Prepare-TransLampResources.ps1 and then .\scripts\Publish-TransLamp.ps1."
        }
    }

    $transLampExecutable = Join-Path $transLampKit 'TransLamp.exe'
    if ($CheckOnly) {
        [pscustomobject]@{ Version = $transLampVersion; Executable = $transLampExecutable; WorkingDirectory = $transLampKit }
        return
    }

    Start-Process -FilePath $transLampExecutable -WorkingDirectory $transLampKit
}
catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
