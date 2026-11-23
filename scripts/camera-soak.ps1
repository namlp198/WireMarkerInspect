param(
    [double]$Minutes = 30,
    [string]$Configuration = "Release",
    [string]$Output
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $Output = Join-Path $repo "artifacts/camera-soak-$stamp.json"
}
$report = [System.IO.Path]::GetFullPath($Output)
$project = Join-Path $repo "src/WireMarkerInspection.Desktop/WireMarkerInspection.Desktop.csproj"
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Desktop build failed with exit code $LASTEXITCODE." }
$exe = Join-Path $repo "src/WireMarkerInspection.Desktop/bin/$Configuration/net8.0-windows/WireMarkerInspection.Desktop.exe"
Write-Output "Soaking the camera for $Minutes minute(s). This contacts real hardware."
$arguments = @("--camera-soak", ('"' + $report + '"'), $Minutes.ToString([System.Globalization.CultureInfo]::InvariantCulture))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
Get-Content -LiteralPath $report
if ($process.ExitCode -ne 0) { throw "Camera soak failed with exit code $($process.ExitCode)." }
Write-Output "Camera soak passed: $report"
