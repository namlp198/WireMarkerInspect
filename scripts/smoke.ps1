param([string]$OutputDirectory,[switch]$Published,[ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts\smoke-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$exe=Join-Path $repo "src\WireMarkerInspection.Desktop\bin\$Configuration\net8.0-windows\WireMarkerInspection.Desktop.exe"
if($Published){$exe=Join-Path $repo 'publish\WireMarkerInspection\WireMarkerInspection.Desktop.exe'}
if(-not(Test-Path -LiteralPath $exe)){throw "Build $Configuration first."}
$process=Start-Process -FilePath $exe -ArgumentList @('--offline-smoke',('"{0}"' -f $OutputDirectory)) -WorkingDirectory $repo -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(30000)){Stop-Process -Id $process.Id;throw 'Offline UI smoke timed out.'}
$result=Join-Path $OutputDirectory 'result.txt'
if(Test-Path -LiteralPath $result){Get-Content -LiteralPath $result}
if($process.ExitCode -ne 0){throw "Offline smoke failed: exit $($process.ExitCode)"}
if(-not(Test-Path -LiteralPath $result)){throw 'Smoke did not produce a result.'}
Write-Output $OutputDirectory
