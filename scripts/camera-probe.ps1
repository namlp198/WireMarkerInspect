param(
    [switch]$Grab,
    [switch]$SoftwareTrigger,
    [string]$Configuration = "Release",
    [string]$Output
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $Output = Join-Path $repo "artifacts/camera-probe-$stamp.json"
}
$report = [System.IO.Path]::GetFullPath($Output)
$project = Join-Path $repo "src/WireMarkerInspection.Desktop/WireMarkerInspection.Desktop.csproj"
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Desktop build failed with exit code $LASTEXITCODE." }
$exe = Join-Path $repo "src/WireMarkerInspection.Desktop/bin/$Configuration/net8.0-windows/WireMarkerInspection.Desktop.exe"
$arguments = @("--camera-probe", ('"' + $report + '"'))
if ($Grab) { $arguments += "--grab" }
elseif ($SoftwareTrigger) { $arguments += "--software-trigger" }
$process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
Get-Content -LiteralPath $report
if ($process.ExitCode -ne 0) { throw "Camera probe failed. See $report" }
Write-Host "Camera probe passed: $report"
