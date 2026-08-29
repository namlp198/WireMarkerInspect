param(
 [string]$ImageDirectory='D:\src\learn_opencv_all\output\Release\wire_marker',
 [string]$OpenCvRoot='D:\opencv',
 [string]$Manifest,
 [string]$OutputCsv
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $Manifest){$Manifest=Join-Path $repo 'tests\real-images.expected.json'}
if(-not $OutputCsv){$OutputCsv=Join-Path $repo 'artifacts\test-results\real-images.csv'}
$cli=Join-Path $repo 'build\native\Release\VisionOcrCli.exe'
$models=Join-Path $repo 'assets\ocr'
foreach($path in @($ImageDirectory,$Manifest,$cli,(Join-Path $models 'detector.onnx'),
 (Join-Path $models 'recognizer.onnx'),(Join-Path $models 'dictionary.txt'))) {
 if(-not(Test-Path -LiteralPath $path)){throw "Required real-image test input is missing: $path"}
}
$spec=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$roi=@($spec.normalizedRoi)
if($roi.Count -ne 4){throw 'Manifest normalizedRoi must contain left, top, right and bottom.'}
$opencvBin=Join-Path $OpenCvRoot 'build\x64\vc16\bin'
if(-not(Test-Path -LiteralPath $opencvBin)){throw "OpenCV runtime directory is missing: $opencvBin"}
$env:PATH="$opencvBin;$env:PATH"
$arguments=@(
 (Join-Path $models 'detector.onnx'),(Join-Path $models 'recognizer.onnx'),
 (Join-Path $models 'dictionary.txt'),$ImageDirectory,[string]$spec.orientation,
 [string]$roi[0],[string]$roi[1],[string]$roi[2],[string]$roi[3]
)
$lines=@(& $cli @arguments)
if($LASTEXITCODE -ne 0){throw "VisionOcrCli failed ($LASTEXITCODE)."}
$actual=@{}
foreach($line in $lines) {
 if([string]::IsNullOrWhiteSpace($line)){continue}
 $item=$line | ConvertFrom-Json
 $actual[$item.file]=$item.result
}
$report=foreach($case in $spec.cases) {
 $result=$actual[$case.file]
 [string[]]$expectedText=@($case.regions)
 [string[]]$actualText=if($null -eq $result){@()}else{@($result.regions | ForEach-Object {$_.text})}
 $textMatches=$expectedText.Count -eq $actualText.Count
 if($textMatches) {
  for($index=0;$index -lt $expectedText.Count;$index++) {
   if($expectedText[$index] -cne $actualText[$index]){$textMatches=$false;break}
  }
 }
 $rotationMatches=$null -ne $result -and [int]$result.rotation -eq [int]$case.rotation
 [pscustomobject]@{
  File=$case.file
  Expected=($expectedText -join ' | ')
  Actual=($actualText -join ' | ')
  ExpectedRotation=[int]$case.rotation
  ActualRotation=if($null -eq $result){$null}else{[int]$result.rotation}
  Result=if($textMatches -and $rotationMatches){'PASS'}else{'FAIL'}
 }
}
$extra=@($actual.Keys | Where-Object {$_ -notin @($spec.cases.file)})
$outputDirectory=Split-Path -Parent $OutputCsv
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$report | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding utf8
$report | Format-Table -AutoSize
$passed=@($report | Where-Object Result -eq 'PASS').Count
Write-Host "Real-image OCR: $passed/$($report.Count) passed. Report: $OutputCsv"
if($extra.Count -gt 0){Write-Warning "Images without ground truth: $($extra -join ', ')"}
if($passed -ne $report.Count){throw 'Real-image OCR exact-match regression detected.'}

$desktop=Join-Path $repo 'src\WireMarkerInspection.Desktop\bin\Release\net8.0-windows\WireMarkerInspection.Desktop.exe'
if(-not(Test-Path -LiteralPath $desktop)){throw "Release desktop build is missing: $desktop"}
$managedOutput=Join-Path (Split-Path -Parent $OutputCsv) 'managed-load'
$process=Start-Process -FilePath $desktop -ArgumentList @(
 '--real-image-smoke',('"{0}"' -f $ImageDirectory),('"{0}"' -f $Manifest),('"{0}"' -f $managedOutput)
) -WorkingDirectory $repo -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(120000)){Stop-Process -Id $process.Id;throw 'Managed Load Image OCR smoke timed out.'}
$managedResult=Join-Path $managedOutput 'managed-load-result.txt'
if(Test-Path -LiteralPath $managedResult){Get-Content -LiteralPath $managedResult}
if($process.ExitCode -ne 0){throw "Managed Load Image OCR smoke failed: exit $($process.ExitCode)"}
if(-not(Test-Path -LiteralPath $managedResult)){throw 'Managed Load Image OCR smoke did not produce a result.'}
