<#
.SYNOPSIS
    A/B start-up comparison of two Holm bench builds, interleaved (A B A B …) so machine load hits both alike.
    Per run: ms until the first visible window, ready time and JIT counts (ready.txt), CPU ms and private MB 11 s after launch. Reports medians.
.EXAMPLE
    pwsh docs/opt/ab-start.ps1 -A out/base-bench/Holm.exe -B out/var-bench/Holm.exe -Pairs 7
#>
param([Parameter(Mandatory)] [string] $A, [Parameter(Mandatory)] [string] $B, [int] $Pairs = 7, [int] $Settle = 11)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/../.."
Add-Type -Namespace OptAb -Name Win -MemberDefinition @'
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
function Median($v) { $s = @($v | Where-Object { $null -ne $_ }) | Sort-Object; if (-not $s) { return $null }; $n = $s.Count; if ($n % 2) { $s[[int](($n - 1) / 2)] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }

function Run([string] $exe) {
    $data = Join-Path $root 'out/opt-data/ab'
    if (Test-Path $data) { Remove-Item -Recurse -Force $data }
    New-Item -ItemType Directory $data | Out-Null
    Copy-Item (Join-Path $root 'out/opt-data/settings.seed.json') "$data/settings.json"
    $env:HOLM_DATA = $data
    $w = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath (Resolve-Path $exe) -PassThru
    try {
        while (-not [OptAb.Win]::HasVisibleWindow([uint32]$p.Id) -and $w.Elapsed.TotalSeconds -lt 30) { Start-Sleep -Milliseconds 5 }
        $start = $w.Elapsed.TotalMilliseconds
        Start-Sleep -Seconds $Settle
        $p.Refresh()
        # ready.txt (bench build): "ready <ms since start> <jit methods> <jit ms>" and the same 10 s after start
        $stamps = @{}
        if (Test-Path "$data/ready.txt") { foreach ($l in Get-Content "$data/ready.txt") { $f = $l -split ' '; $stamps[$f[0]] = $f } }
        [pscustomobject]@{
            StartMs = [math]::Round($start); CpuMs = [math]::Round($p.TotalProcessorTime.TotalMilliseconds); PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
            ReadyMs = $(if ($stamps['ready']) { [double]$stamps['ready'][1] }); JitAtReady = $(if ($stamps['ready']) { [double]$stamps['ready'][2] })
            Jit10s = $(if ($stamps['10s']) { [double]$stamps['10s'][2] }); JitMs10s = $(if ($stamps['10s']) { [double]$stamps['10s'][3] })
        }
    }
    finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(10000) | Out-Null }; Remove-Item Env:HOLM_DATA }
}

Run $A | Out-Null; Run $B | Out-Null   # warm-up, not counted
$ra = @(); $rb = @()
foreach ($i in 1..$Pairs) { $ra += Run $A; $rb += Run $B }
foreach ($x in @(@{ N = 'A'; R = $ra; E = $A }, @{ N = 'B'; R = $rb; E = $B })) {
    [pscustomobject]@{
        Build = "$($x.N) $($x.E)"
        StartMs = Median ($x.R.StartMs); ReadyMs = Median ($x.R.ReadyMs); CpuMs11s = Median ($x.R.CpuMs); PrivateMB11s = Median ($x.R.PrivateMB)
        JitAtReady = Median ($x.R.JitAtReady); Jit10s = Median ($x.R.Jit10s); JitMs10s = Median ($x.R.JitMs10s)
        Runs = ($x.R | ForEach-Object { "$($_.ReadyMs)/$($_.CpuMs)" }) -join ' '
    }
}
