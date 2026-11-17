# Runs every test suite and prints one RESULT line (AvaloniaTests in parallel with the LayoutTests shards).
#
#   pwsh tools/Run-Tests.ps1 [-Framework net11.0|net10.0|both] [-MaxParallel n] [-Build] [-E2E]
#   pwsh tools/Run-Tests.ps1 -Changed [-Since <git ref>] [-Framework ...] [-Build] [-List]   (the quick loop; -List: only show the groups)
#
# -Changed runs only the test groups the changes touch, and stops at the first failure: the files changed since -Since
# (default: the upstream branch - so uncommitted and unpushed work), untracked ones too. A changed test file runs its own
# group; a changed source file the groups whose tests name a type it declares; a changed sample or schema the groups
# that name it. A change deep inside (a layout fix) can reach groups that do not name it: the full run (no -Changed)
# is still what "done" means.
#
# The WinForms LayoutTests take most of the time in a few heavy groups (each checks many forms at several DPIs), so
# the run is split into shards, each its own process (--only / --skip): those groups each on their own, the rest
# together. AvaloniaTests runs alongside (one framework after the other), and the shards run one after another: LayoutTests
# processes must not overlap. Its DPI emulation sends WM_DPICHANGED to off-screen windows; with two or more of these
# processes at once the heavy shards stalled (a form kept re-measuring its minimum size after each DPI change), with
# one at a time never. -MaxParallel raises the count, for investigating that.
# Live: each case is printed as it starts, failures and finished shards at once, and ##PROGRESS## lines feed the
# GUI (pwsh tools/Test-GUI.ps1: tools/AI_FGF.TestRunner, a console over this script).
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
    [switch]$E2E,
    # only the groups the changes touch, stopping at the first failure (see above)
    [switch]$Changed,
    # with -Changed: the git ref the changes are counted from (default: the upstream branch, else HEAD)
    [string]$Since = "",
    # stop at the first failure (implied by -Changed)
    [switch]$FailFast,
    # with -Changed: only print the groups it would run
    [switch]$List
)
if ($Changed) { $FailFast = $true }
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

# -Changed: which groups the changes touch
function Select-Changed {
    $base = $Since
    if (-not $base) { $base = (git -C $root rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null); if ($LASTEXITCODE -ne 0 -or -not $base) { $base = "HEAD" } }
    $files = @(git -C $root diff --name-only --relative $base 2>$null) + @(git -C $root ls-files --others --exclude-standard 2>$null) |
             Where-Object { $_ -and $_ -notmatch '(^|/)(bin|obj|artifacts|internal)/' } | Sort-Object -Unique
    Write-Host "Changed   $($files.Count) file(s) since $base"
    function Types($text) { [regex]::Matches($text, '\b(?:class|record|struct|enum|interface)\s+([A-Z]\w+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique }
    function Mentions($text, $names) { foreach ($n in $names) { if ($text -match "\b$([regex]::Escape($n))\b") { return $n } } return $null }
    $read = @{}
    function Text($path) { if (-not $read.ContainsKey($path)) { $read[$path] = Get-Content -Raw -LiteralPath $path -ErrorAction SilentlyContinue }; $read[$path] }

    # the names of what changed: in a changed .cs file, the identifiers on its changed lines (a new file: the types it
    # declares) - not every type of the file: AiFgf or FormScaffold are named by every test; for any other file its
    # name and its folder's. The test harness itself (Harness.cs, Program.cs) selects nothing: any run exercises it.
    $frameworkTypes = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($src in Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
        foreach ($t in Types (Text $src.FullName)) { [void]$frameworkTypes.Add($t) }
    }
    $names = @(); $changedTests = @()
    $untracked = @(git -C $root ls-files --others --exclude-standard 2>$null)
    foreach ($f in $files) {
        $full = Join-Path $root $f
        if ($f -match '(Harness|Program)\.cs$' -and $f -like 'tests/*') { continue }
        if ($f -like 'tests/*' -and $f -like '*.cs') {
            # a test file selects its own group; a helper of the tests (a form, a generator) the groups that use its types
            $changedTests += $full
            if ((Test-Path $full) -and (Text $full) -notmatch '\bclass\s+\w*Tests\b') { $names += Types (Text $full) }
            continue
        }
        if ($f -like '*.cs') {
            if ($untracked -contains $f -and (Test-Path $full)) { $names += Types (Text $full); continue }
            # from its changed lines: the framework's own types they use and the members they declare - not every word on
            # them ("Action", "Column" named half the groups); and the file's own types (a fix inside ParamTable)
            $lines = @(git -C $root diff -U0 $base -- $f 2>$null) | Where-Object { $_ -match '^[+-]' -and $_ -notmatch '^(\+\+\+|---)' }
            foreach ($l in $lines) {
                $code = $l.Substring(1) -replace '//.*$', ''
                $names += [regex]::Matches($code, '\b[A-Z][A-Za-z0-9_]{3,}\b') | ForEach-Object { $_.Value } | Where-Object { $frameworkTypes.Contains($_) }
                $names += [regex]::Matches($code, '\b(?:public|internal|protected)\b[^=(;{]*?\b([A-Z]\w{3,})\s*(?:\(|\{|=>|=|;)') | ForEach-Object { $_.Groups[1].Value }
            }
            if (Test-Path $full) { $names += Types (Text $full) }
        }
        else
        {
            $names += [IO.Path]::GetFileName($f)
            $dir = [IO.Path]::GetDirectoryName($f)
            if ($dir -match '[\/]') { $names += [IO.Path]::GetFileName($dir) }   # a sample's folder (AI_FGF.Template), not "tools" or "src"
        }
    }
    $names = @($names | Where-Object { $_ } | Sort-Object -Unique)
    $srcTypes = @($names)
    # the groups: LayoutTests - the classes Program.cs runs with Safe(X.Run); AvaloniaTests - Run("name", X.Method)
    $sel = @{ Layout = @(); Avalonia = @() }
    foreach ($suite in @(@{ Key = "Layout"; Dir = "tests/AI_FGF.LayoutTests"; Pattern = 'Safe\((\w+)\.\w+\)' },
                         @{ Key = "Avalonia"; Dir = "tests/AI_FGF.AvaloniaTests"; Pattern = 'Run\("([^"]+)",\s*(\w+)\.' })) {
        $dir = Join-Path $root $suite.Dir
        $program = Text (Join-Path $dir "Program.cs")
        $sources = Get-ChildItem $dir -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
        # a name more than 15 % of the groups mention says nothing about which ones a change touches
        $groupTexts = @{}
        foreach ($m in [regex]::Matches($program, $suite.Pattern)) {
            $cls = if ($suite.Key -eq "Layout") { $m.Groups[1].Value } else { $m.Groups[2].Value }
            $groupTexts[$m.Groups[1].Value] = (@($sources | Where-Object { (Text $_.FullName) -match "\bclass\s+$cls\b" }) | ForEach-Object { Text $_.FullName }) -join "`n"
        }
        $specific = @($srcTypes | Where-Object { $n = $_; @($groupTexts.Values | Where-Object { $_ -match "\b$([regex]::Escape($n))\b" }).Count -le [Math]::Max(1, 0.15 * $groupTexts.Count) })
        foreach ($m in [regex]::Matches($program, $suite.Pattern)) {
            $cls = if ($suite.Key -eq "Layout") { $m.Groups[1].Value } else { $m.Groups[2].Value }
            $group = $m.Groups[1].Value
            $own = @($sources | Where-Object { (Text $_.FullName) -match "\bclass\s+$cls\b" })
            $text = ($own | ForEach-Object { Text $_.FullName }) -join "`n"
            $why = if ($own | Where-Object { $changedTests -contains $_.FullName }) { "its test changed" } elseif ($n = Mentions $text $specific) { "names $n" } else { $null }
            if ($why) { $sel[$suite.Key] += $group; if ($List) { Write-Host "          $($suite.Key) $group - $why" } }
        }
        $sel[$suite.Key] = @($sel[$suite.Key] | Sort-Object -Unique)
    }
    return $sel
}
$selected = $null
if ($Changed) {
    $selected = Select-Changed
    Write-Host "Groups    LayoutTests: $(if ($selected.Layout.Count) { $selected.Layout -join ', ' } else { '-' })"
    Write-Host "          AvaloniaTests: $(if ($selected.Avalonia.Count) { $selected.Avalonia -join ', ' } else { '-' })"
    if ($List) { exit 0 }
    if ($selected.Layout.Count + $selected.Avalonia.Count -eq 0) { Write-Host "RESULT: PASS - no test group touches the changes"; exit 0 }
}
$ff = if ($FailFast) { @("--fail-fast") } else { @() }

$jobs = foreach ($tfm in $frameworks) {
    $layout = Join-Path $root "tests/AI_FGF.LayoutTests/bin/Debug/$tfm-windows/AI_FGF.LayoutTests.exe"
    $avalonia = Join-Path $root "tests/AI_FGF.AvaloniaTests/bin/Debug/$tfm/AI_FGF.AvaloniaTests.exe"
    foreach ($exe in $layout, $avalonia) { if (-not (Test-Path $exe)) { Write-Host "RESULT: FAIL - $exe not found (build first, or pass -Build)"; exit 1 } }
    if ($selected) {
        # the quick loop: one process per suite with just the touched groups
        if ($selected.Layout.Count) { [pscustomobject]@{ Name = "$tfm layout (changed)"; Exe = $layout; Args = @("--only", ($selected.Layout -join ",")) + $ff; Log = "$tfm-layout-changed.log" } }
        if ($selected.Avalonia.Count) { [pscustomobject]@{ Name = "$tfm avalonia (changed)"; Exe = $avalonia; Args = @("--only", ($selected.Avalonia -join ",")) + $ff; Log = "$tfm-avalonia-changed.log" } }
        continue
    }
    foreach ($group in $heavy) { [pscustomobject]@{ Name = "$tfm layout $group"; Exe = $layout; Args = @("--only", $group) + $ff; Log = "$tfm-layout-$group.log" } }
    [pscustomobject]@{ Name = "$tfm layout (the rest)"; Exe = $layout; Args = @("--skip", ($heavy -join ",")) + $ff; Log = "$tfm-layout-rest.log" }
    [pscustomobject]@{ Name = "$tfm avalonia"; Exe = $avalonia; Args = $ff; Log = "$tfm-avalonia.log" }
}

$clock = [Diagnostics.Stopwatch]::StartNew()
# Live progress: each shard's log is read as it grows; its "=== case ===" lines and FAIL lines are printed at once, a
# finished shard's result too, and "##PROGRESS##|activity|percent|status" lines (what a ToolForm shows as its progress
# bar) and the ##PLAN##/##SHARD## lines tools/AI_FGF.TestRunner shows estimate how far the run is from the case counts of the previous run's logs.
function Expected($j) {
    $old = Join-Path $logs $j.Log
    if (Test-Path $old) { @(Select-String -Path $old -Pattern '^=== ' -ErrorAction SilentlyContinue).Count } else { 0 }
}
foreach ($j in $jobs) { $j | Add-Member -NotePropertyName Expected -NotePropertyValue (Expected $j) -Force }
# for a GUI (tools/AI_FGF.TestRunner): the plan, then ##SHARD##|name|running and ##SHARD##|name|ok/fail|passed|total|seconds
foreach ($j in $jobs) { Write-Host "##PLAN##|$($j.Name)|$($j.Expected)" }
if ($E2E) { foreach ($tfm in $frameworks) { Write-Host "##PLAN##|$tfm e2e|0" } }
function Start-Shard($j) {
    $log = Join-Path $logs $j.Log
    $p = Start-Process -FilePath $j.Exe -ArgumentList $j.Args -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru
    Write-Host "  ...  $($j.Name) started"
    Write-Host "##SHARD##|$($j.Name)|running"
    [pscustomobject]@{ Job = $j; Process = $p; Log = $log; Offset = 0L; Partial = ""; Cases = 0; Last = ""; Text = [System.Text.StringBuilder]::new(); Reported = $false }
}
function Read-New($r) {
    if (-not (Test-Path $r.Log)) { return }
    try {
        $fs = [System.IO.File]::Open($r.Log, 'Open', 'Read', 'ReadWrite')
        try {
            [void]$fs.Seek($r.Offset, 'Begin')
            $chunk = (New-Object System.IO.StreamReader($fs)).ReadToEnd()
            $r.Offset = $fs.Length
        } finally { $fs.Dispose() }
    } catch { return }
    if (-not $chunk) { return }
    [void]$r.Text.Append($chunk)
    $lines = ($r.Partial + $chunk) -split "`r?`n"
    $r.Partial = $lines[-1]
    foreach ($line in $lines[0..($lines.Count - 2)]) {
        # a FAIL line: case-sensitive and a whole word - -match 'FAIL' also took every "Failed: 0" summary for a failure
        if ($line -match '^=== (.*) ===$') { $r.Cases++; $r.Last = $Matches[1]; Write-Host "  ·    [$($r.Job.Name)] $($Matches[1])" }
        elseif ($line -cmatch '\bFAIL\b') { Write-Host "  FAIL [$($r.Job.Name)] $($line.Trim())" }
    }
}
$checks = 0; $failed = 0; $bad = @()
# a process killed by Windows exits with an NTSTATUS: name the common ones (a bare -1073740940 says nothing)
function Describe-Exit([int]$code) {
    $known = @{ 0xC0000005 = "access violation"; 0xC0000374 = "heap corruption"; 0xC00000FD = "stack overflow";
                0xC0000409 = "fail-fast / stack buffer overrun"; 0xE0434352 = "unhandled .NET exception"; 0xC000013A = "interrupted (Ctrl+C)" }
    # (PowerShell reads 0xC0000374 as the Int32 -1073740940 - the same value an exit code arrives as)
    if ($known.ContainsKey($code)) { return "$code (0x$($code.ToString('X8')) $($known[$code]))" }
    return "$code"
}
function Report($r) {
    Read-New $r
    $r.Reported = $true
    $text = $r.Text.ToString()
    $err = Get-Content "$($r.Log).err" -Raw -ErrorAction SilentlyContinue
    Remove-Item "$($r.Log).err" -ErrorAction SilentlyContinue
    $m = [regex]::Match($text, 'Checks: (\d+)\s+Passed: (\d+)\s+Failed: (\d+)')
    $ok = $r.Process.ExitCode -eq 0 -and $m.Success
    if ($m.Success) { $script:checks += [int]$m.Groups[1].Value; $script:failed += [int]$m.Groups[3].Value }
    $summary = if ($m.Success) { "$($m.Groups[2].Value)/$($m.Groups[1].Value)" } else { "no summary" }
    Write-Host ("  {0,-4} {1,-38} {2,10}  exit {3}  ({4:F0} s)" -f $(if ($ok) { "ok" } else { "FAIL" }), $r.Job.Name, $summary, (Describe-Exit $r.Process.ExitCode), ($r.Process.ExitTime - $r.Process.StartTime).TotalSeconds)
    $seconds = [int]($r.Process.ExitTime - $r.Process.StartTime).TotalSeconds
    Write-Host ("##SHARD##|{0}|{1}|{2}|{3}|{4}" -f $r.Job.Name, $(if ($ok) { "ok" } else { "fail" }), $(if ($m.Success) { $m.Groups[2].Value } else { 0 }), $(if ($m.Success) { $m.Groups[1].Value } else { 0 }), $seconds)
    if (-not $ok) {
        $script:bad += $r.Job.Name
        if ($err) { Write-Host "         stderr: $($err.Trim() -split "`n" | Select-Object -First 3)" }
    }
}
# two queues side by side: the LayoutTests shards (-MaxParallel at a time) and the AvaloniaTests runs (one at a
# time: both frameworks write the same screenshot files)
$queue = [System.Collections.Generic.Queue[object]]::new([object[]]@($jobs | Where-Object { $_.Name -notlike "* avalonia" }))
$avQueue = [System.Collections.Generic.Queue[object]]::new([object[]]@($jobs | Where-Object { $_.Name -like "* avalonia" }))
$layout = @(); $avalonia = @(); $started = @(); $lastProgress = ""
while ($queue.Count -gt 0 -or $avQueue.Count -gt 0 -or @($started | Where-Object { -not $_.Reported }).Count -gt 0) {
    while ($queue.Count -gt 0 -and @($layout | Where-Object { -not $_.Process.HasExited }).Count -lt $MaxParallel) {
        $r = Start-Shard $queue.Dequeue(); $layout += $r; $started += $r
    }
    if ($avQueue.Count -gt 0 -and @($avalonia | Where-Object { -not $_.Process.HasExited }).Count -eq 0) {
        $r = Start-Shard $avQueue.Dequeue(); $avalonia += $r; $started += $r
    }
    foreach ($r in $started | Where-Object { -not $_.Reported }) {
        if ($r.Process.HasExited) { Report $r } else { Read-New $r }
    }
    if ($FailFast -and $bad.Count -gt 0) {
        # -FailFast: a failed shard ends the run - the others are stopped, the queued ones never start
        foreach ($r in $started | Where-Object { -not $_.Reported }) { try { $r.Process.Kill() } catch { }; $r.Reported = $true; Write-Host "  stop $($r.Job.Name) (an earlier shard failed)" }
        $queue.Clear(); $avQueue.Clear()
        break
    }
    # progress: a finished shard counts 1, a running one its share of the previous run's cases
    $done = 0.0
    foreach ($r in $started) { $done += if ($r.Reported) { 1 } elseif ($r.Job.Expected -gt 0) { [Math]::Min(0.95, $r.Cases / $r.Job.Expected) } else { 0 } }
    $pct = [int][Math]::Floor(100 * $done / $jobs.Count)
    $now = @($started | Where-Object { -not $_.Reported } | ForEach-Object { "$($_.Job.Name): $($_.Last)" }) -join "; "
    $progress = "##PROGRESS##|Testing ($([int]$clock.Elapsed.TotalMinutes) min)|$pct|$now"
    if ($progress -ne $lastProgress) { Write-Host $progress; $lastProgress = $progress }
    Start-Sleep -Milliseconds 500
}

if ($E2E) {
    foreach ($tfm in $frameworks) {
        $e2e = Join-Path $root "tests/AI_FGF.E2ETests/bin/Debug/$tfm-windows/AI_FGF.E2ETests.exe"
        $log = Join-Path $logs "$tfm-e2e.log"
        Write-Host "##SHARD##|$tfm e2e|running"
        $e2eClock = [Diagnostics.Stopwatch]::StartNew()
        & $e2e *> $log
        $code = $LASTEXITCODE
        $m = [regex]::Match((Get-Content $log -Raw), 'Checks: (\d+)\s+Passed: (\d+)\s+Failed: (\d+)')
        if ($m.Success) { $checks += [int]$m.Groups[1].Value; $failed += [int]$m.Groups[3].Value }
        Write-Host ("  {0,-4} {1,-38} {2,10}  exit {3}" -f $(if ($code -eq 0) { "ok" } else { "FAIL" }), "$tfm e2e", $(if ($m.Success) { "$($m.Groups[2].Value)/$($m.Groups[1].Value)" } else { "no summary" }), $code)
        Write-Host ("##SHARD##|{0} e2e|{1}|{2}|{3}|{4}" -f $tfm, $(if ($code -eq 0) { "ok" } else { "fail" }), $(if ($m.Success) { $m.Groups[2].Value } else { 0 }), $(if ($m.Success) { $m.Groups[1].Value } else { 0 }), [int]$e2eClock.Elapsed.TotalSeconds)
        if ($code -ne 0) { $bad += "$tfm e2e" }
    }
}

# how long each shard took, merged into artifacts/test-logs/durations.json: the estimate of the next run's time left
$durationsFile = Join-Path $logs "durations.json"
$durations = @{}
if (Test-Path $durationsFile) { try { (Get-Content $durationsFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $durations[$_.Name] = [int]$_.Value } } catch { } }
foreach ($r in $started | Where-Object { $_.Process.HasExited }) { $durations[$r.Job.Name] = [int]($r.Process.ExitTime - $r.Process.StartTime).TotalSeconds }
$durations | ConvertTo-Json | Set-Content -Path $durationsFile -Encoding UTF8

Write-Host ("Checks: {0}   Failed: {1}   ({2:F0} s; logs in {3})" -f $checks, $failed, $clock.Elapsed.TotalSeconds, $logs)
if ($bad.Count -gt 0) { Write-Host "RESULT: FAIL - $($bad -join ', ')"; exit 1 }
Write-Host "RESULT: PASS"
exit 0
