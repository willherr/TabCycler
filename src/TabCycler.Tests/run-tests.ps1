# Builds and runs the TabCycler test suite.
#
#   pwsh -NoProfile -File .\run-tests.ps1                all tests
#   pwsh -NoProfile -File .\run-tests.ps1 -Filter Hold   only matching names
#
# Uses the .NET Framework compiler that ships with Windows, so there is nothing
# to install and no external test package. Exits nonzero on any failure, which
# is what CI keys off.
param([string]$Filter)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

$exe = Join-Path $PSScriptRoot 'TabCycler.Tests.exe'
if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }

# The engine and its interface are compiled straight into the test binary, so
# the tests exercise the shipping source rather than a copy of it. Win32Platform
# comes too, because PlatformTests calls it for real: a wrong DllImport compiles
# cleanly and only fails at call time.
$sources = @(
    (Join-Path $PSScriptRoot '..\TabCycler\WatchState.cs')
    (Join-Path $PSScriptRoot '..\TabCycler\InputKind.cs')
    (Join-Path $PSScriptRoot '..\TabCycler\InputOptions.cs')
    (Join-Path $PSScriptRoot '..\TabCycler\IPlatform.cs')
    (Join-Path $PSScriptRoot '..\TabCycler\CyclerEngine.cs')
    (Join-Path $PSScriptRoot '..\TabCycler\Settings.cs')
    (Join-Path $PSScriptRoot '..\TabCycler\Win32Platform.cs')
    (Join-Path $PSScriptRoot 'TestHarness.cs')
    (Join-Path $PSScriptRoot 'FakePlatform.cs')
    (Join-Path $PSScriptRoot 'EngineTests.cs')
    (Join-Path $PSScriptRoot 'SettingsTests.cs')
    (Join-Path $PSScriptRoot 'PlatformTests.cs')
    (Join-Path $PSScriptRoot 'TestProgram.cs')
)

$missing = $sources | Where-Object { -not (Test-Path -LiteralPath $_) }
if ($missing) { throw "missing source files:`n$($missing -join "`n")" }

$compilerArgs = @(
    '/nologo'
    '/target:exe'
    '/platform:anycpu'
    '/optimize+'
    '/warnaserror+'
    '/out:' + $exe
    '/reference:System.dll'
    '/reference:System.Core.dll'
) + $sources

& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "test compile failed (exit $LASTEXITCODE)" }
if (-not (Test-Path -LiteralPath $exe)) { throw "test compile produced no exe" }

Write-Host ''
if ($Filter) { & $exe $Filter } else { & $exe }
exit $LASTEXITCODE
