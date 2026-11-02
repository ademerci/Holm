<#
.SYNOPSIS
    Measures what an idle Holm costs: memory and CPU time while nothing happens.

.DESCRIPTION
    Starts Holm.exe, lets it settle, then reads its memory and how much CPU time it used over a quiet minute.
    Holm is stopped afterwards. Run it with nothing playing and the mouse away from the notch.

.EXAMPLE
    ./tools/Measure-Footprint.ps1 -Exe publish/Holm.exe
    ./tools/Measure-Footprint.ps1 -Exe publish/Holm.exe -Environment @{ DOTNET_GCgen0size = '0x400000' }
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Label = (Split-Path $Exe -Leaf),
    [int] $WarmupSeconds = 25,
    [int] $SampleSeconds = 45,
    [hashtable] $Environment = @{}
)

$ErrorActionPreference = 'Stop'
Get-Process Holm -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$saved = @{}
foreach ($name in $Environment.Keys) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, [string]$Environment[$name])
}
try { $process = Start-Process -FilePath (Resolve-Path $Exe) -PassThru }
finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) } }

try {
    Start-Sleep -Seconds $WarmupSeconds
    $process.Refresh()
    if ($process.HasExited) { throw "Holm exited during start-up (code $($process.ExitCode))" }
    $startCpu = $process.TotalProcessorTime

    Start-Sleep -Seconds $SampleSeconds
    $process.Refresh()
    if ($process.HasExited) { throw "Holm exited while being measured (code $($process.ExitCode))" }
    $idleCpu = $process.TotalProcessorTime - $startCpu

    [pscustomobject]@{
        Variant        = $Label
        'Private MB'   = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        'Working MB'   = [math]::Round($process.WorkingSet64 / 1MB, 1)
        'Peak WS MB'   = [math]::Round($process.PeakWorkingSet64 / 1MB, 1)
        'Start CPU ms' = [math]::Round($startCpu.TotalMilliseconds)
        'Idle CPU ms/min' = [math]::Round($idleCpu.TotalMilliseconds * 60 / $SampleSeconds)
        Threads        = $process.Threads.Count
        Handles        = $process.HandleCount
    }
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(10000) | Out-Null }
}
