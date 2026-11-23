param([string]$Version='0.1.0',[switch]$RequireOcrAssets)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$artifacts=Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$buildArguments=@{Configuration='Release';Publish=$true;RequireOcrAssets=$RequireOcrAssets}
& (Join-Path $PSScriptRoot 'build.ps1') @buildArguments *> (Join-Path $artifacts 'release-build.log')
Write-Output 'Build and publish: PASS'
& (Join-Path $PSScriptRoot 'test.ps1') *> (Join-Path $artifacts 'release-tests.log')
Write-Output 'Managed and native tests: PASS'
& (Join-Path $PSScriptRoot 'smoke.ps1') -Published -OutputDirectory (Join-Path $artifacts 'release-smoke')
& (Join-Path $PSScriptRoot 'package.ps1') -Version $Version *> (Join-Path $artifacts 'release-package.log')
Write-Output 'Installer compilation: PASS (not installed)'
Get-Item -LiteralPath (Join-Path $repo "dist\WireMarkerInspection-Setup-$Version.exe") | Select-Object FullName,Length,LastWriteTime
