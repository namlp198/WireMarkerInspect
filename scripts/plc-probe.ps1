param(
    [Parameter(Mandatory=$true)][string]$ReadAddress,
    [string]$WriteAddress,
    [string]$Configuration = "Release",
    [string]$Output
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $Output = Join-Path $repo "artifacts/plc-probe-$stamp.json"
}
$report = [System.IO.Path]::GetFullPath($Output)
$project = Join-Path $repo "src/WireMarkerInspection.Desktop/WireMarkerInspection.Desktop.csproj"
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Desktop build failed with exit code $LASTEXITCODE." }
$exe = Join-Path $repo "src/WireMarkerInspection.Desktop/bin/$Configuration/net8.0-windows/WireMarkerInspection.Desktop.exe"
Write-Output "Reading $ReadAddress from the PLC configured in settings.json. This contacts real hardware."
if ($WriteAddress) { Write-Warning "Also pulsing $WriteAddress. A PLC write can move machinery - only run this when the line is safe." }
$arguments = @("--plc-probe", ('"' + $report + '"'), $ReadAddress)
if ($WriteAddress) { $arguments += $WriteAddress }
$process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
Get-Content -LiteralPath $report
if ($process.ExitCode -ne 0) { throw "PLC probe failed with exit code $($process.ExitCode)." }
Write-Output "PLC probe passed: $report"
