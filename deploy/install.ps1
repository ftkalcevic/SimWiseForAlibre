<#
.SYNOPSIS
    Builds and installs SimWiseForAlibre into Alibre Design.

.DESCRIPTION
    1. Builds the add-on (net48 x64) into out\<Configuration>\.
    2. Copies SimWiseForAlibre.dll, .adc and .ico to %ProgramData%\Alibre AddOns\SimWiseForAlibre\.
    3. Registers the add-on under HKLM\SOFTWARE\Alibre Design Add-Ons
       (value name = add-on GUID, data = install folder).

    Steps 2 and 3 need administrator rights; the script re-launches itself elevated for them.
    Close Alibre Design first (the DLL is locked while Alibre runs), and restart it
    afterwards - Alibre only reads add-ons at startup.

    SimWise 4D must be installed: the add-on uses its SimWiseExport.dll.

.PARAMETER Configuration
    Build configuration to install: Release (default) or Debug.
#>
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$AddOnName = 'SimWiseForAlibre'
$AddOnGuid = '{E0602E34-4ADD-4C82-B046-F3E93CE6B907}'   # must match SimWiseForAlibre.adc
$RegKey    = 'SOFTWARE\Alibre Design Add-Ons'

$projectDir = Split-Path -Parent $PSScriptRoot
$outDir     = Join-Path $projectDir "out\$Configuration"

# --- Build (as the user, before elevating) --------------------------------------
if (-not $SkipBuild) {
    dotnet build (Join-Path $projectDir "src\$AddOnName") -c $Configuration -o $outDir
    if ($LASTEXITCODE -ne 0) { throw 'The add-on failed to build.' }
}

# --- Elevation ---------------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Administrator rights are required - relaunching elevated...'
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
                 '-Configuration', $Configuration, '-SkipBuild')
    Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait
    exit
}

try {
    $files = @("$AddOnName.dll", "$AddOnName.adc", "$AddOnName.ico")
    foreach ($f in $files) {
        if (-not (Test-Path (Join-Path $outDir $f))) {
            throw "Missing '$f' in '$outDir'. Run this script without -SkipBuild."
        }
    }

    if (Get-Process -Name 'Alibre Design' -ErrorAction SilentlyContinue) {
        throw 'Alibre Design is running. Close it and run this again.'
    }

    $targetDir = Join-Path $env:ProgramData "Alibre AddOns\$AddOnName"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    foreach ($f in $files) {
        Copy-Item -Path (Join-Path $outDir $f) -Destination $targetDir -Force
    }
    $pdb = Join-Path $outDir "$AddOnName.pdb"
    if (Test-Path $pdb) { Copy-Item -Path $pdb -Destination $targetDir -Force }
    Write-Host "Copied the add-on to $targetDir"

    # Use the .NET API: New-Item -Force on HKLM:\SOFTWARE fails even when elevated.
    $key = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($RegKey)
    try {
        $key.SetValue($AddOnGuid, $targetDir, [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally {
        $key.Close()
    }
    Write-Host "Registered $AddOnGuid -> $targetDir under HKLM\$RegKey"

    Write-Host ''
    Write-Host 'Done. Start Alibre Design, open a part or assembly, and use SimWise > Export to SimWise...' -ForegroundColor Green
}
catch {
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
}

if ($Host.Name -eq 'ConsoleHost') {
    Write-Host ''
    Read-Host 'Press Enter to close'
}
