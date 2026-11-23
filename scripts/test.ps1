param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $repo 'WireMarkerInspection.sln') -c $Configuration --no-build --logger "trx;LogFileName=managed-$Configuration.trx" --results-directory (Join-Path $repo 'artifacts\test-results')
if($LASTEXITCODE -ne 0){throw 'Managed tests failed.'}
ctest --test-dir (Join-Path $repo 'build\native') -C $Configuration --output-on-failure
if($LASTEXITCODE -ne 0){throw 'Native tests failed.'}
