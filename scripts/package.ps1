param([string]$Version='0.1.0')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$publish=Join-Path $repo 'publish\WireMarkerInspection'
if(-not(Test-Path -LiteralPath (Join-Path $publish 'WireMarkerInspection.Desktop.exe'))){throw 'Run build.ps1 -Publish first.'}
$iscc=Get-Command ISCC.exe -ErrorAction SilentlyContinue
if($iscc){$compiler=$iscc.Source}else{$compiler=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Inno Setup 6\ISCC.exe'}
if(-not(Test-Path -LiteralPath $compiler)){throw 'Install Inno Setup 6 to create the installer.'}
& $compiler "/DAppVersion=$Version" (Join-Path $repo 'installer\WireMarkerInspection.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
