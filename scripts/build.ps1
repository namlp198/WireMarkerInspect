param(
 [ValidateSet('Debug','Release')][string]$Configuration='Release',
 [string]$OpenCvRoot='D:\opencv',
 [string]$OrtRoot='D:\src\learn_opencv_all\Libraries\onnxruntime-win-x64-gpu-1.17.3',
 [switch]$Publish,
 [switch]$RequireOcrAssets,
 [switch]$NativeOnly
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
function Invoke-Checked([string]$Executable,[string[]]$Arguments) {
 & $Executable @Arguments
 if($LASTEXITCODE -ne 0){throw "$Executable failed ($LASTEXITCODE)"}
}
$vswhere=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vs){throw 'Visual Studio C++ tools are required.'}
$cmake=Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if(-not(Test-Path -LiteralPath $cmake)){$cmake=(Get-Command cmake -ErrorAction Stop).Source}
$generator=if($vs -match '\\18\\'){'Visual Studio 18 2026'}else{'Visual Studio 17 2022'}
$native=Join-Path $repo 'build\native'
Invoke-Checked $cmake @('-S',(Join-Path $repo 'native\WireMarkerInspection.Vision.Native'),'-B',$native,'-G',$generator,'-A','x64',"-DOpenCV_DIR=$OpenCvRoot/build","-DONNXRUNTIME_ROOT=$OrtRoot")
Invoke-Checked $cmake @('--build',$native,'--config',$Configuration)
$opencvDll=Get-ChildItem -LiteralPath (Join-Path $OpenCvRoot 'build\x64\vc16\bin') -Filter 'opencv_world*.dll' |
 Where-Object {if($Configuration -eq 'Debug'){$_.Name -match 'd\.dll$'}else{$_.Name -notmatch 'd\.dll$'}} |
 Select-Object -First 1
if(-not $opencvDll){throw "OpenCV $Configuration runtime DLL not found."}
Copy-Item -LiteralPath $opencvDll.FullName -Destination (Join-Path $native $Configuration)
$licenses=Join-Path $repo 'assets\licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
foreach($entry in @(@((Join-Path $OpenCvRoot 'LICENSE.txt'),'OpenCV.txt'),@((Join-Path $OrtRoot 'LICENSE'),'ONNXRuntime.txt'))) {
 if(Test-Path -LiteralPath $entry[0]){Copy-Item -LiteralPath $entry[0] -Destination (Join-Path $licenses $entry[1])}
}
if($RequireOcrAssets) {
 foreach($name in @('detector.onnx','recognizer.onnx','dictionary.txt')) {
  if(-not(Test-Path -LiteralPath (Join-Path $repo "assets\ocr\$name"))){throw "Missing production OCR asset: $name"}
 }
}
if($NativeOnly){return}
Invoke-Checked 'dotnet' @('build',(Join-Path $repo 'WireMarkerInspection.sln'),'-c',$Configuration,'--nologo','-p:SkipNativeBuild=true')
if($Publish) {
 Invoke-Checked 'dotnet' @('publish',(Join-Path $repo 'src\WireMarkerInspection.Desktop'),'-c','Release','-r','win-x64','--self-contained','true','-o',(Join-Path $repo 'publish\WireMarkerInspection'),'-p:SkipNativeBuild=true')
 $manifest=[ordered]@{createdUtc=[DateTime]::UtcNow.ToString('O');ocrAssetsPresent=((Test-Path (Join-Path $repo 'assets\ocr\detector.onnx')) -and (Test-Path (Join-Path $repo 'assets\ocr\recognizer.onnx')) -and (Test-Path (Join-Path $repo 'assets\ocr\dictionary.txt')));hardwareValidated=$false;note='Development build. See README and handoff for acceptance limits.'}
 $manifest|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $repo 'publish\WireMarkerInspection\build-status.json')
}
