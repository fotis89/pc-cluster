<#
.SYNOPSIS
  Measures Cluster's real footprint: CPU % (of the whole machine) and memory, sampled once a second.
.EXAMPLE
  .\tools\measure.ps1 -Seconds 120
#>
param(
    [int]$Seconds = 120,
    [string]$Name = 'Cluster'
)

$p = Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "$Name is not running"; exit 1 }

$cores = [Environment]::ProcessorCount
$cpu = New-Object System.Collections.Generic.List[double]
$ws = New-Object System.Collections.Generic.List[double]
$priv = New-Object System.Collections.Generic.List[double]

$lastCpu = $p.TotalProcessorTime.TotalSeconds
$lastTime = [Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt $Seconds; $i++) {
    Start-Sleep -Seconds 1
    $p.Refresh()
    if ($p.HasExited) { Write-Error "$Name exited"; exit 1 }
    $nowCpu = $p.TotalProcessorTime.TotalSeconds
    $elapsed = $lastTime.Elapsed.TotalSeconds
    $lastTime.Restart()
    $cpu.Add(($nowCpu - $lastCpu) / $elapsed / $cores * 100)
    $lastCpu = $nowCpu
    $ws.Add($p.WorkingSet64 / 1MB)
    $priv.Add($p.PrivateMemorySize64 / 1MB)
}

function Stat($list) {
    $m = $list | Measure-Object -Average -Maximum
    [pscustomobject]@{ Average = [math]::Round($m.Average, 2); Max = [math]::Round($m.Maximum, 2) }
}

[pscustomobject]@{
    Seconds          = $Seconds
    Cores            = $cores
    'CPU % avg'      = (Stat $cpu).Average
    'CPU % max'      = (Stat $cpu).Max
    'Working set MB' = (Stat $ws).Average
    'Private MB'     = (Stat $priv).Average
    'Private MB max' = (Stat $priv).Max
    Threads          = $p.Threads.Count
    Handles          = $p.HandleCount
} | Format-List
