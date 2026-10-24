<#
  UI and page smoke tests: every window opened off-screen on a real view model (with the review scenarios), and the
  browser pages served by the real web server and screenshotted with headless Edge.

    pwsh tools\smoke.ps1                 # writes screenshots to artifacts\smoke
    pwsh tools\smoke.ps1 -Out somewhere

  Exit code 0 when everything passed. CI uploads the screenshots so a reviewer can look at them.
#>
param([string] $Out = 'artifacts\smoke')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force $Out | Out-Null
$out = (Resolve-Path $Out).Path
$failed = @()

Write-Host '==> Building the smoke harnesses' -ForegroundColor Cyan
dotnet build tests\Stagewatch.UiSmoke -c Debug -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'UI smoke build failed' }
dotnet build tests\Stagewatch.PageShot -c Debug -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Page smoke build failed' }

# One window per process (WPF's Application can only run once per process).
foreach ($mode in 'main', 'main-ro', 'settings', 'screens', 'screens-windows', 'copilot', 'slides-agent', 'rundown-editor', 'vm', 'review', 'panels') {
    Write-Host "==> UI smoke: $mode" -ForegroundColor Cyan
    dotnet run --project tests\Stagewatch.UiSmoke -c Debug --no-build -- $out $mode
    if ($LASTEXITCODE -ne 0) { $failed += "ui:$mode" }
}

Write-Host '==> Page smoke' -ForegroundColor Cyan
dotnet run --project tests\Stagewatch.PageShot -c Debug --no-build -- $out
if ($LASTEXITCODE -ne 0) { $failed += 'pages' }

if ($failed.Count -gt 0) { Write-Host "Smoke failures: $($failed -join ', ')" -ForegroundColor Red; exit 1 }
Write-Host "==> All smoke tests passed (screenshots in $out)" -ForegroundColor Green
