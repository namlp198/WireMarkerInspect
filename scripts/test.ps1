$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $repo 'WireMarkerInspection.sln') -c Release --no-build --logger 'trx;LogFileName=managed.trx' --results-directory (Join-Path $repo 'artifacts\test-results')
if($LASTEXITCODE -ne 0){throw 'Managed tests failed.'}
ctest --test-dir (Join-Path $repo 'build\native') -C Release --output-on-failure
if($LASTEXITCODE -ne 0){throw 'Native tests failed.'}
