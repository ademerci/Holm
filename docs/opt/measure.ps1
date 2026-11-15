<#
.SYNOPSIS
    Optimisation baseline/after measurement for Holm (docs/opt). Every value is measured 3 times; the median is reported.

.DESCRIPTION
    Same method before and after. Holm instances started here run on test data (HOLM_DATA, bench build only), offline
    (updates, weather location, Beam and friends pointed away from the internet) and beside the user's own Holm:
    only processes this script started are stopped.

.EXAMPLE
    pwsh docs/opt/measure.ps1 -Out docs/opt/measure-before.json
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [int] $Runs = 3,
    [switch] $SkipBuildTimes,
    [switch] $UseExisting,  # measure the exes already in -ReleaseDir and -BenchDir (no publish)
    [string] $ReleaseDir = 'out/m-release',
    [string] $BenchDir = 'out/m-bench'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/../.."
Set-Location $root
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet-sdk10"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Median([double[]] $v) { $s = $v | Sort-Object; $n = $s.Count; if ($n % 2) { $s[[int](($n - 1) / 2)] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }
function Clean { foreach ($d in 'WinNotch/bin', 'WinNotch/obj', 'WinNotch.Tests/bin', 'WinNotch.Tests/obj') { if (Test-Path $d) { Remove-Item -Recurse -Force $d } } }
function Timed([scriptblock] $b) { $w = [Diagnostics.Stopwatch]::StartNew(); & $b | Out-Null; if ($LASTEXITCODE) { throw "failed: $b" }; $w.Elapsed.TotalSeconds }

$common = @('-c', 'Release', '-r', 'win-x64', '-p:SelfContained=true', '-p:PublishSingleFile=true', '-p:PublishReadyToRun=true',
            '-p:DebugType=none', '-p:IncludeNativeLibrariesForSelfExtract=true')
$result = [ordered]@{ Date = (Get-Date -Format s); Commit = (git rev-parse --short HEAD) }

# --- Build, publish and test times (clean each time) ---
if (-not $SkipBuildTimes) {
    $build = foreach ($i in 1..$Runs) { Clean; Timed { dotnet build WinNotch -c Release -v q } }
    $publish = foreach ($i in 1..$Runs) { Clean; Timed { dotnet publish WinNotch @common -o $ReleaseDir -v q } }
    dotnet build WinNotch.Tests -c Release -v q | Out-Null
    $test = foreach ($i in 1..$Runs) {
        Timed { dotnet test WinNotch.Tests -c Release --no-build --filter "Category!=Local&Category!=Online&Category!=Manual" -- xUnit.ParallelizeTestCollections=false }
    }
    $result.BuildSeconds = @{ Runs = $build; Median = Median $build }
    $result.PublishSeconds = @{ Runs = $publish; Median = Median $publish }
    $result.TestSeconds = @{ Runs = $test; Median = Median $test }
}
elseif (-not $UseExisting) { dotnet publish WinNotch @common -o $ReleaseDir -v q | Out-Null }

# --- Size of what ships ---
$exe = Get-Item "$ReleaseDir/Holm.exe"
$zipDir = 'out/m-zip'; if (Test-Path $zipDir) { Remove-Item -Recurse -Force $zipDir }
New-Item -ItemType Directory $zipDir | Out-Null
Copy-Item $exe.FullName "$zipDir/Holm.exe"; Copy-Item LICENSE "$zipDir/LICENSE"; Set-Content "$zipDir/README.txt" "Holm for Windows x64" -NoNewline
if (Test-Path out/m.zip) { Remove-Item out/m.zip }
Compress-Archive -Path "$zipDir/*" -DestinationPath out/m.zip
$result.ExeMB = [math]::Round($exe.Length / 1MB, 2)
$result.ZipMB = [math]::Round((Get-Item out/m.zip).Length / 1MB, 2)

# --- Bench build (release + bench switch) for live runs ---
if (-not $UseExisting) { dotnet publish WinNotch @common -p:HolmBench=true -o $BenchDir -v q | Out-Null }
$bench = (Resolve-Path "$BenchDir/Holm.exe").Path

function NewData([string] $name) {
    $d = Join-Path $root "out/opt-data/$name"
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
    New-Item -ItemType Directory $d | Out-Null
    Copy-Item out/opt-data/settings.seed.json "$d/settings.json"
    $d
}

# The notch is a tool window, so Process.MainWindowHandle stays 0: start time = first visible top-level window of the process.
Add-Type -Namespace Opt -Name Win -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
public static bool HasVisibleWindow(uint pid) {
    bool found = false;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { found = true; return false; } return true; }, System.IntPtr.Zero);
    return found;
}
'@

$result | ConvertTo-Json -Depth 6 | Set-Content $Out   # keep what's measured even if a live run is blocked

# --- Start time and idle footprint ---
# Warm-up run first (disk cache, native library unpacking); it does not count.
$live = foreach ($i in 0..$Runs) {
    $env:HOLM_DATA = NewData "run$i"
    $w = [Diagnostics.Stopwatch]::StartNew()
    try { $p = Start-Process -FilePath $bench -PassThru }
    catch { Write-Warning "live run blocked: $($_.Exception.Message)"; Remove-Item Env:HOLM_DATA; continue }
    try {
        while (-not [Opt.Win]::HasVisibleWindow([uint32]$p.Id) -and $w.Elapsed.TotalSeconds -lt 30) { Start-Sleep -Milliseconds 5 }
        if ($w.Elapsed.TotalSeconds -ge 30) { throw 'no window within 30 s' }
        $startMs = $w.Elapsed.TotalMilliseconds
        Start-Sleep -Seconds 25
        # Ready = Loaded and the first frame done, UI thread idle for the first time (bench build stamp, App.xaml.cs).
        $ready = (Get-Content "$env:HOLM_DATA/ready.txt" | Where-Object { $_ -like 'ready *' } | Select-Object -First 1) -split ' '
        $readyMs = [double]$ready[1]; $jitReady = [double]$ready[2]
        $p.Refresh(); if ($p.HasExited) { throw "exited during start-up ($($p.ExitCode))" }
        $cpu0 = $p.TotalProcessorTime
        $startCpu = $cpu0.TotalMilliseconds
        Start-Sleep -Seconds 45
        $p.Refresh(); if ($p.HasExited) { throw "exited while measured ($($p.ExitCode))" }
        [pscustomobject]@{
            StartMs = [math]::Round($startMs); ReadyMs = [math]::Round($readyMs); JitAtReady = $jitReady; StartCpuMs = [math]::Round($startCpu)
            IdleCpuMsPerMin = [math]::Round(($p.TotalProcessorTime - $cpu0).TotalMilliseconds * 60 / 45)
            PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); WorkingMB = [math]::Round($p.WorkingSet64 / 1MB, 1)
            PeakWorkingMB = [math]::Round($p.PeakWorkingSet64 / 1MB, 1); Threads = $p.Threads.Count; Handles = $p.HandleCount
        }
    }
    finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(10000) | Out-Null }; Remove-Item Env:HOLM_DATA }
}
$live = @($live | Select-Object -Skip 1)
if ($live.Count -eq 0) { $result.Live = 'not measurable: Smart App Control blocked the new bench exe'; $result | ConvertTo-Json -Depth 6 | Set-Content $Out; exit 0 }
$result.Live = [ordered]@{ Runs = $live }
foreach ($k in 'StartMs', 'ReadyMs', 'JitAtReady', 'StartCpuMs', 'IdleCpuMsPerMin', 'PrivateMB', 'WorkingMB', 'PeakWorkingMB', 'Threads', 'Handles') {
    $result.Live[$k] = Median ($live | ForEach-Object { $_.$k })
}

# --- Open/close smoothness and cost (MainWindow.Bench.cs, quick mode) ---
$env:HOLM_BENCH_QUICK = '1'
$benchLogs = foreach ($i in 1..$Runs) {
    $env:HOLM_DATA = NewData "bench$i"
    $log = Join-Path $root "out/m-bench-$i.txt"
    if (Test-Path $log) { Remove-Item $log }
    $p = Start-Process -FilePath $bench -ArgumentList '--bench', "`"$log`"", '10' -PassThru
    if (-not $p.WaitForExit(120000)) { Stop-Process -Id $p.Id -Force; throw 'bench hung' }
    Remove-Item Env:HOLM_DATA
    Get-Content $log -Raw
}
Remove-Item Env:HOLM_BENCH_QUICK
function Pick([string] $pattern) { Median ($benchLogs | ForEach-Object { [double]([regex]::Match($_, $pattern).Groups[1].Value) }) }
$result.Bench = [ordered]@{
    OpenCallMs = Pick 'open : call median ([\d.]+)'; OpenLongestFrameMs = Pick 'open : call median [\d.]+ ms \(max [\d.]+\), longest frame median ([\d.]+)'
    CloseCallMs = Pick 'close: call median ([\d.]+)'; CpuPerRoundMs = Pick '\(([\d.]+) ms each\)'
    OpenSkipped = Pick 'open : .*skipped frames (\d+)'; CloseSkipped = Pick 'close: .*skipped frames (\d+)'
    Logs = $benchLogs
}

# --- Source size ---
$src = Get-ChildItem WinNotch -Recurse -File -Include *.cs, *.xaml | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$result.SourceFiles = $src.Count
$result.SourceLines = ($src | ForEach-Object { (Get-Content $_.FullName).Count } | Measure-Object -Sum).Sum
$assets = Get-ChildItem WinNotch/Assets, WinNotch/notch.ico -Recurse -File
$result.AssetsKB = [math]::Round(($assets | Measure-Object Length -Sum).Sum / 1KB, 1)
$result.PackageReferences = ([xml](Get-Content WinNotch/WinNotch.csproj)).Project.ItemGroup.PackageReference.Include | Where-Object { $_ }

$result | ConvertTo-Json -Depth 6 | Set-Content $Out
$result | ConvertTo-Json -Depth 3 | Write-Output
