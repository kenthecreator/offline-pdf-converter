param(
  [Parameter(Mandatory=$true)][string]$Destination,
  [string]$SourceDirectory = ''
)
$ErrorActionPreference = 'Stop'
# Build-time only. Never download or install a runtime on an end user's PC.
if (-not $SourceDirectory) {
  $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
  if (-not (Test-Path $vswhere)) { throw 'Install Visual Studio C++ build tools on the build machine, or set WindowsCrtSourceDirectory to its x64 release CRT redist folder.' }
  $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
  if (-not $installation) { throw 'Visual Studio C++ build tools were not found.' }
  $candidates = Get-ChildItem (Join-Path $installation 'VC\Redist\MSVC') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+(\.\d+)?$' } |
    Sort-Object { [version]$_.Name } -Descending
  foreach ($candidate in $candidates) {
    $crt = Get-ChildItem (Join-Path $candidate.FullName 'x64') -Directory -Filter 'Microsoft.VC*.CRT' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($crt) { $SourceDirectory = $crt.FullName; break }
  }
}
if (-not $SourceDirectory -or -not (Test-Path $SourceDirectory)) { throw 'No redistributable x64 release CRT directory was found.' }
$source = (Resolve-Path $SourceDirectory).Path
if ($source -match 'debug_nonredist' -or (Split-Path $source -Leaf) -notmatch '^Microsoft\.VC\d+\.CRT$') {
  throw 'Use the unmodified Microsoft.VC*.CRT release folder from a licensed Visual Studio redist directory.'
}
$files = @(Get-ChildItem $source -File -Filter '*.dll')
foreach ($required in @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')) {
  if ($required -notin $files.Name) { throw "Required redistributable missing: $required" }
}
# Validate before writing the staging folder; never copy from System32 or a DLL download site.
foreach ($file in $files) {
  $bytes = [IO.File]::ReadAllBytes($file.FullName)
  if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw "Not a PE file: $($file.Name)" }
  $pe = [BitConverter]::ToInt32($bytes, 0x3c)
  if ($pe -lt 0 -or $pe + 6 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $pe) -ne 0x4550 -or [BitConverter]::ToUInt16($bytes, $pe + 4) -ne 0x8664) {
    throw "Not an x64 PE DLL: $($file.Name)"
  }
  if ($file.VersionInfo.CompanyName -notmatch 'Microsoft') { throw "Not a Microsoft runtime: $($file.Name)" }
}
New-Item -ItemType Directory -Force $Destination | Out-Null
Get-ChildItem $Destination -File -Filter '*.dll' | Remove-Item -Force
$records = @()
foreach ($file in $files) {
  $target = Join-Path $Destination $file.Name
  Copy-Item $file.FullName $target -Force
  $algorithm = [Security.Cryptography.SHA256]::Create()
  $stream = [IO.File]::OpenRead($target)
  try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
  finally { $stream.Dispose(); $algorithm.Dispose() }
  $records += [ordered]@{ file = $file.Name; version = $file.VersionInfo.FileVersion; sha256 = $hash }
}
$manifest = [ordered]@{ source = $source; files = $records } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $Destination 'manifest.json'), $manifest, (New-Object Text.UTF8Encoding($false)))
Write-Host "Staged $($files.Count) unmodified x64 CRT DLLs for embedding; no runtime installer is packaged."
