$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$artifacts=Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -Publish *> (Join-Path $artifacts 'release-build.log')
Write-Output 'Build and publish: PASS'
& (Join-Path $PSScriptRoot 'test.ps1') *> (Join-Path $artifacts 'release-tests.log')
Write-Output 'Managed and native tests: PASS'
& (Join-Path $PSScriptRoot 'smoke.ps1') -Published -OutputDirectory (Join-Path $artifacts 'release-smoke')
& (Join-Path $PSScriptRoot 'package.ps1') -Version '0.1.0' *> (Join-Path $artifacts 'release-package.log')
Write-Output 'Installer compilation: PASS (not installed)'
Get-Item -LiteralPath (Join-Path $repo 'dist\WireMarkerInspection-Setup-0.1.0.exe') | Select-Object FullName,Length,LastWriteTime
