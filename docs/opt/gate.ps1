<#
.SYNOPSIS
    The optimisation gate: build (warnings are errors = typecheck + lint), tests, smoke test. Exit code 0 = green.

.DESCRIPTION
    1. Release build of app and tests with -warnaserror.
    2. The CI test suite (same filter as .github/workflows/test.yml).
    3. Smoke: Debug build renders every notch, pop-out, Holm window page and settings state with `--snap` on test data
       (HOLM_DATA, beside the user's own Holm). Green when it exits by itself with all 70 pictures.
    Pictures land in out/snap-gate (or -SnapTo).
#>
param([string] $SnapTo = 'out/snap-gate', [int] $ExpectedPictures = 70)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet-sdk10"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Step([string] $name, [scriptblock] $b) {
    Write-Host "== $name"
    & $b
    if ($LASTEXITCODE) { Write-Host "GATE RED: $name" -ForegroundColor Red; exit 1 }
}

Step 'build (Release, warnings as errors)' { dotnet build WinNotch.Tests -c Release -warnaserror -v q }
Step 'tests' { dotnet test WinNotch.Tests -c Release --no-build --filter "Category!=Local&Category!=Online&Category!=Manual" -- xUnit.ParallelizeTestCollections=false }
Step 'build (Debug)' { dotnet build WinNotch -c Debug -warnaserror -v q }

Write-Host '== smoke (--snap on test data)'
$data = 'out/opt-data/gate'
if (Test-Path $data) { Remove-Item -Recurse -Force $data }
New-Item -ItemType Directory $data | Out-Null
Copy-Item out/opt-data/settings.seed.json "$data/settings.json"
if (Test-Path $SnapTo) { Remove-Item -Recurse -Force $SnapTo }
New-Item -ItemType Directory $SnapTo | Out-Null
$env:HOLM_DATA = (Resolve-Path $data).Path
$dll = 'WinNotch/bin/Debug/net10.0-windows10.0.19041.0/Holm.dll'
$p = Start-Process dotnet -ArgumentList $dll, '--snap', "`"$((Resolve-Path $SnapTo).Path)`"" -PassThru -WindowStyle Hidden
$ok = $p.WaitForExit(300000)
Remove-Item Env:HOLM_DATA
if (-not $ok) { Stop-Process -Id $p.Id -Force; Write-Host 'GATE RED: smoke hung' -ForegroundColor Red; exit 1 }
$count = (Get-ChildItem $SnapTo -Filter *.png).Count
if ($p.ExitCode -ne 0 -or $count -lt $ExpectedPictures) {
    Write-Host "GATE RED: smoke exit $($p.ExitCode), $count of $ExpectedPictures pictures" -ForegroundColor Red; exit 1
}
if (Test-Path "$data/error.log") { Write-Host "GATE RED: smoke wrote error.log`n$(Get-Content "$data/error.log" -Raw)" -ForegroundColor Red; exit 1 }
Write-Host "GATE GREEN ($count pictures)" -ForegroundColor Green
