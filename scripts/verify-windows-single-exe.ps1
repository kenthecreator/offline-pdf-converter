param(
  [Parameter(Mandatory=$true)][string]$ExePath,
  [switch]$BlockNetwork,
  [switch]$VerifyGui,
  [string]$ReportPath = ''
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $ExePath).Path
$testDirectory = Join-Path $env:TEMP ("OfflinePDFConverter-clean-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $testDirectory | Out-Null
$rules = @()
$profiles = @()
try {
  # Verify the actual distribution filename, including spaces and parentheses.
  $testName = 'Offline PDF Converter (v4.2).exe'
  $isolatedExe = Join-Path $testDirectory $testName
  Copy-Item $source $isolatedExe
  if ((Get-ChildItem $testDirectory -File).Count -ne 1) { throw 'Expected only one executable.' }
  $report = Join-Path $testDirectory 'report.json'
  if ($BlockNetwork) {
    # For disposable Windows CI/VM hosts. Keep the runner online; isolate only the tested exe.
    if ((Get-Service MpsSvc).Status -ne 'Running') { throw 'Windows Firewall service is not running; offline test cannot be established.' }
    $profiles = @(Get-NetFirewallProfile | Select-Object Name, Enabled)
    Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled True
    if (@(Get-NetFirewallProfile -PolicyStore ActiveStore | Where-Object { $_.Enabled -ne 'True' }).Count) { throw 'Firewall profiles are not all enabled.' }
    foreach ($direction in @('Inbound', 'Outbound')) {
      $name = 'OfflinePDFConverter-test-' + [Guid]::NewGuid().ToString('N')
      $program = [IO.Path]::GetFullPath($isolatedExe)
      New-NetFirewallRule -DisplayName $name -Direction $direction -Action Block -Program $program -Enabled True -Profile Any | Out-Null
      $rules += $name
      $active = Get-NetFirewallRule -DisplayName $name -PolicyStore ActiveStore
      if ($active.Enabled -ne 'True' -or $active.Action -ne 'Block') { throw 'Network block rule is not active.' }
      $filter = $active | Get-NetFirewallApplicationFilter
      $ruleProgram = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($filter.Program))
      if (-not $ruleProgram.Equals($program, [StringComparison]::OrdinalIgnoreCase)) { throw "Network block rule targets a different executable: $ruleProgram; expected $program" }
    }
    Write-Host 'NETWORK ISOLATION: active inbound/outbound block rules for the tested exe; all firewall profiles enabled.'
  }
  # Restrict PATH to Windows itself. No installed Tesseract is visible or used.
  $previousPath = $env:PATH
  $previousTessdata = $env:TESSDATA_PREFIX
  $previousBundleDirectory = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
  try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    $env:TESSDATA_PREFIX = Join-Path $testDirectory 'missing-data'
    # A fresh private directory avoids accidentally testing an older extracted bundle.
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $testDirectory 'bundle'
    $mode = if ($VerifyGui) { '--verify-gui' } else { '--verify-offline' }
    $process = Start-Process $isolatedExe -WorkingDirectory $testDirectory -ArgumentList @($mode, ('"' + $report + '"')) -PassThru
    if (-not $process.WaitForExit(300000)) { $process.Kill(); throw 'Self-test timed out.' }
    if (-not (Test-Path $report)) { throw 'No self-test report was produced.' }
    $result = Get-Content $report -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $result.passed) { throw (Get-Content $report -Raw) }
    if ((Get-FileHash $source).Hash -ne (Get-FileHash $isolatedExe).Hash) { throw 'Executable modified during verification.' }
    if (@(Get-ChildItem $testDirectory -File -Filter '*.dll').Count) { throw 'Verification required loose DLLs beside the exe.' }
    $result | Add-Member -NotePropertyName networkIsolation -NotePropertyValue $(if ($BlockNetwork) { 'Windows Firewall: tested exe blocked inbound and outbound on all profiles' } else { 'not enforced' })
    $result | Add-Member -NotePropertyName freshBundleExtraction -NotePropertyValue $true
    $result | ConvertTo-Json -Depth 8 | Write-Output
    if ($ReportPath) {
      $destination = [IO.Path]::GetFullPath($ReportPath)
      New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
      $result | ConvertTo-Json -Depth 8 | Set-Content $destination -Encoding utf8
      if ($VerifyGui -and (Test-Path ([IO.Path]::ChangeExtension($report, ".png")))) { Copy-Item ([IO.Path]::ChangeExtension($report, ".png")) ([IO.Path]::ChangeExtension($destination, ".png")) }
    }
  } finally { $env:PATH = $previousPath; $env:TESSDATA_PREFIX = $previousTessdata; $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousBundleDirectory }
} finally {
  foreach ($name in $rules) { Remove-NetFirewallRule -DisplayName $name -ErrorAction Continue }
  foreach ($profile in $profiles) { Set-NetFirewallProfile -Profile $profile.Name -Enabled $profile.Enabled -ErrorAction Continue }
  Remove-Item $testDirectory -Recurse -Force
}
