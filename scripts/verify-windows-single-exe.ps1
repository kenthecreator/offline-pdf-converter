param([Parameter(Mandatory=$true)][string]$ExePath)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $ExePath).Path
$testDirectory = Join-Path $env:TEMP ("OfflinePDFConverter-clean-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $testDirectory | Out-Null
try {
  $isolatedExe = Join-Path $testDirectory 'Offline PDF Converter v3.2.0.exe'
  Copy-Item $source $isolatedExe
  if ((Get-ChildItem $testDirectory -File).Count -ne 1) { throw 'Expected only one executable.' }
  $report = Join-Path $testDirectory 'report.json'
  # Restrict PATH to Windows itself. No installed Tesseract is visible or used.
  $previousPath = $env:PATH
  $previousTessdata = $env:TESSDATA_PREFIX
  try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    $env:TESSDATA_PREFIX = Join-Path $testDirectory 'missing-data'
    $process = Start-Process $isolatedExe -ArgumentList @('--verify-offline', ('"' + $report + '"')) -PassThru
    if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'Self-test timed out.' }
    if (-not (Test-Path $report)) { throw 'No self-test report was produced.' }
    $result = Get-Content $report -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $result.passed) { throw (Get-Content $report -Raw) }
    Get-Content $report -Raw
  } finally { $env:PATH = $previousPath; $env:TESSDATA_PREFIX = $previousTessdata }
} finally { Remove-Item $testDirectory -Recurse -Force }
