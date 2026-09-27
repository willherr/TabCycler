# Install and refresh the running copy of Tab Cycler.
#
# Use this after every new version so the copy you actually run (and any taskbar
# pin pointing at it) is the newest build:
#
#     pwsh -NoProfile -File .\install.ps1
#
# It builds, copies the exe to a stable per-user location, stops whatever is
# running, and starts the new one from that location. The path never changes, so
# anything pinned to it keeps working across versions.
#
# The stable location is deliberately NOT the old C:\Users\wch\Tools\TabCycler
# scratch folder, which still holds the original single-file source and dev
# scripts. Reusing it is how a stale binary ended up being the thing that ran.
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\TabCycler"
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$built = Join-Path $repo 'src\TabCycler\TabCycler.exe'
$installed = Join-Path $InstallDir 'TabCycler.exe'

Write-Host "building..."
& pwsh -NoProfile -File (Join-Path $repo 'src\TabCycler\build.ps1')
if ($LASTEXITCODE -ne 0) { throw "build failed" }
if (-not (Test-Path -LiteralPath $built)) { throw "no exe was produced at $built" }

Write-Host "installing to $installed ..."
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -LiteralPath $built -Destination $installed -Force

Write-Host "stopping any running copy ..."
Get-Process TabCycler -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  stopping pid $($_.Id)"
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Milliseconds 500

Write-Host "starting the installed copy ..."
Start-Process -FilePath $installed
Start-Sleep -Seconds 2

$proc = Get-Process TabCycler -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw "the installed copy did not start" }
$actual = $proc.Path
Write-Host "running: pid $($proc.Id) responding=$($proc.Responding)"
Write-Host "path:   $actual"
if ($actual -ne $installed) {
    Write-Warning "running from $($actual) rather than the installed path; the taskbar pin would follow the wrong file"
}
Write-Host "done. If you have not pinned it yet: right-click the Tab Cycler taskbar button -> Pin to taskbar. It pins this path, so future installs stay current."
