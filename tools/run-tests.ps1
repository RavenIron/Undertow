# Runs the off-game test harnesses.
# Usage, from the solution root:  .\tools\run-tests.ps1
#
# These do NOT launch Valheim. They compile the mod's real source against stubs and run
# it on net8, so the logic that fails silently (drift math, serialization, config
# migration) is checkable in about two seconds.

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'

$harnesses = @(
    'tests\CoreTests\CoreTests.csproj'
)

$anyFailed = $false

foreach ($h in $harnesses) {
    $path = Join-Path $root $h
    if (-not (Test-Path $path)) {
        Write-Host "Missing harness: $h" -ForegroundColor Yellow
        continue
    }

    Write-Host ""
    Write-Host "=== $h ===" -ForegroundColor Cyan

    dotnet run --project $path --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { $anyFailed = $true }
}

Write-Host ""
if ($anyFailed) {
    Write-Host "TESTS FAILED" -ForegroundColor Red
    exit 1
} else {
    Write-Host "All harnesses passed." -ForegroundColor Green
}
