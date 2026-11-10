<#
.SYNOPSIS
    Regenerates lib\Interop.SimWiseExportLib.dll from the installed SimWiseExport.dll.

.DESCRIPTION
    SimWise doesn't register its export type library for x64, so the project can't use a
    COMReference; this imports the type library embedded in the DLL with TlbImp instead.
    Run it after installing a new SimWise version, then rebuild.
#>
param(
    [string]$SimWiseBin = 'C:\Program Files\DST\SimWise 4D for Alibre V9.86\Program\Code\bin'
)

$ErrorActionPreference = 'Stop'

$tlbimp = Get-ChildItem 'C:\Program Files (x86)\Microsoft SDKs\Windows' -Recurse -Filter TlbImp.exe -ErrorAction SilentlyContinue |
          Where-Object { $_.DirectoryName -like '*\x64' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $tlbimp) { throw 'TlbImp.exe (x64) not found. Install the .NET Framework SDK (Visual Studio installs it).' }

$source = Join-Path $SimWiseBin 'SimWiseExport.dll'
if (-not (Test-Path $source)) { throw "SimWiseExport.dll not found in $SimWiseBin" }

$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Interop.SimWiseExportLib.dll'
& $tlbimp.FullName $source /out:$out /namespace:SimWiseExportLib /machine:X64 /nologo
if ($LASTEXITCODE -ne 0) { throw 'TlbImp failed.' }
Write-Host "Wrote $out"
