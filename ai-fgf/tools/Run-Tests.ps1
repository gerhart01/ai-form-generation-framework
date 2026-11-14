# Runs every test suite and prints one RESULT line (AvaloniaTests in parallel with the LayoutTests shards).
#
#   pwsh tools/Run-Tests.ps1 [-Framework net11.0|net10.0|both] [-MaxParallel n] [-Build] [-E2E]
#
# The WinForms LayoutTests take most of the time in a few heavy groups (each checks many forms at several DPIs), so
# the run is split into shards, each its own process (--only / --skip): those groups each on their own, the rest
# together. AvaloniaTests runs alongside (one framework after the other), and the shards run one after another: LayoutTests
# processes must not overlap. Its DPI emulation sends WM_DPICHANGED to off-screen windows; with two or more of these
# processes at once the heavy shards stalled (a form kept re-measuring its minimum size after each DPI change), with
# one at a time never. -MaxParallel raises the count, for investigating that.
# Logs: artifacts/test-logs/<framework>-<shard>.log. Exit code 0 = every shard passed.
param(
    [ValidateSet("net11.0", "net10.0", "both")]
    [string]$Framework = "both",
    # LayoutTests processes at a time (AvaloniaTests always runs alongside); keep 1, see above
    [ValidateRange(1, 32)]
    [int]$MaxParallel = 1,
    # build the solution first (Debug)
    [switch]$Build,
    # also run the end-to-end tests (they drive the demo app; run after the rest, not in parallel)
    [switch]$E2E
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$logs = Join-Path $root "artifacts/test-logs"
New-Item -ItemType Directory -Force -Path $logs | Out-Null

if ($Build) {
    dotnet build (Join-Path $root "AI_FGF.slnx") -c Debug -nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Host "RESULT: FAIL - the build failed"; exit 1 }
}

# the heaviest LayoutTests groups, one process each; everything else runs in one more process
$heavy = @("SpacingTests", "StressTests", "RandomTests", "TextVisibilityTests")
$frameworks = if ($Framework -eq "both") { @("net11.0", "net10.0") } else { @($Framework) }

$jobs = foreach ($tfm in $frameworks) {
    $layout = Join-Path $root "tests/AI_FGF.LayoutTests/bin/Debug/$tfm-windows/AI_FGF.LayoutTests.exe"
    $avalonia = Join-Path $root "tests/AI_FGF.AvaloniaTests/bin/Debug/$tfm/AI_FGF.AvaloniaTests.exe"
    foreach ($exe in $layout, $avalonia) { if (-not (Test-Path $exe)) { Write-Host "RESULT: FAIL - $exe not found (build first, or pass -Build)"; exit 1 } }
    foreach ($group in $heavy) { [pscustomobject]@{ Name = "$tfm layout $group"; Exe = $layout; Args = @("--only", $group); Log = "$tfm-layout-$group.log" } }
    [pscustomobject]@{ Name = "$tfm layout (the rest)"; Exe = $layout; Args = @("--skip", ($heavy -join ",")); Log = "$tfm-layout-rest.log" }
    [pscustomobject]@{ Name = "$tfm avalonia"; Exe = $avalonia; Args = @(); Log = "$tfm-avalonia.log" }
}

$clock = [Diagnostics.Stopwatch]::StartNew()
function Start-Shard($j) {
    $log = Join-Path $logs $j.Log
    $p = Start-Process -FilePath $j.Exe -ArgumentList $j.Args -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru
    [pscustomobject]@{ Job = $j; Process = $p; Log = $log }
}
# two queues side by side: the LayoutTests shards (-MaxParallel at a time) and the AvaloniaTests runs (one at a
# time: both frameworks write the same screenshot files)
$queue = [System.Collections.Generic.Queue[object]]::new([object[]]@($jobs | Where-Object { $_.Name -notlike "* avalonia" }))
$avQueue = [System.Collections.Generic.Queue[object]]::new([object[]]@($jobs | Where-Object { $_.Name -like "* avalonia" }))
$layout = @(); $avalonia = @(); $started = @()
while ($queue.Count -gt 0 -or $avQueue.Count -gt 0) {
    while ($queue.Count -gt 0 -and @($layout | Where-Object { -not $_.Process.HasExited }).Count -lt $MaxParallel) {
        $r = Start-Shard $queue.Dequeue(); $layout += $r; $started += $r
    }
    if ($avQueue.Count -gt 0 -and @($avalonia | Where-Object { -not $_.Process.HasExited }).Count -eq 0) {
        $r = Start-Shard $avQueue.Dequeue(); $avalonia += $r; $started += $r
    }
    Start-Sleep -Milliseconds 300
}
$running = $jobs | ForEach-Object { $j = $_; $started | Where-Object { $_.Job -eq $j } }

$checks = 0; $failed = 0; $bad = @()
foreach ($r in $running) {
    $r.Process.WaitForExit()
    $text = Get-Content $r.Log -Raw -ErrorAction SilentlyContinue
    $err = Get-Content "$($r.Log).err" -Raw -ErrorAction SilentlyContinue
    Remove-Item "$($r.Log).err" -ErrorAction SilentlyContinue
    $m = [regex]::Match("$text", 'Checks: (\d+)\s+Passed: (\d+)\s+Failed: (\d+)')
    $ok = $r.Process.ExitCode -eq 0 -and $m.Success
    if ($m.Success) { $checks += [int]$m.Groups[1].Value; $failed += [int]$m.Groups[3].Value }
    $summary = if ($m.Success) { "$($m.Groups[2].Value)/$($m.Groups[1].Value)" } else { "no summary" }
    Write-Host ("  {0,-4} {1,-38} {2,10}  exit {3}" -f $(if ($ok) { "ok" } else { "FAIL" }), $r.Job.Name, $summary, $r.Process.ExitCode)
    if (-not $ok) {
        $bad += $r.Job.Name
        "$text" -split "`n" | Where-Object { $_ -match 'FAIL' } | Select-Object -First 10 | ForEach-Object { Write-Host "         $($_.Trim())" }
        if ($err) { Write-Host "         stderr: $($err.Trim() -split "`n" | Select-Object -First 3)" }
    }
}

if ($E2E) {
    foreach ($tfm in $frameworks) {
        $e2e = Join-Path $root "tests/AI_FGF.E2ETests/bin/Debug/$tfm-windows/AI_FGF.E2ETests.exe"
        $log = Join-Path $logs "$tfm-e2e.log"
        & $e2e *> $log
        $code = $LASTEXITCODE
        $m = [regex]::Match((Get-Content $log -Raw), 'Checks: (\d+)\s+Passed: (\d+)\s+Failed: (\d+)')
        if ($m.Success) { $checks += [int]$m.Groups[1].Value; $failed += [int]$m.Groups[3].Value }
        Write-Host ("  {0,-4} {1,-38} {2,10}  exit {3}" -f $(if ($code -eq 0) { "ok" } else { "FAIL" }), "$tfm e2e", $(if ($m.Success) { "$($m.Groups[2].Value)/$($m.Groups[1].Value)" } else { "no summary" }), $code)
        if ($code -ne 0) { $bad += "$tfm e2e" }
    }
}

Write-Host ("Checks: {0}   Failed: {1}   ({2:F0} s; logs in {3})" -f $checks, $failed, $clock.Elapsed.TotalSeconds, $logs)
if ($bad.Count -gt 0) { Write-Host "RESULT: FAIL - $($bad -join ', ')"; exit 1 }
Write-Host "RESULT: PASS"
exit 0
