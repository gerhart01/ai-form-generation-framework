# The test run in a window: tools/AI_FGF.TestRunner (a console on the board layout over Run-Tests.ps1): Run/Cancel,
# one row per shard with its own progress and current case, the run as a whole, failures in the log at once.
#
#   pwsh tools/Test-GUI.ps1
#
# It runs from a copy in artifacts/test-runner, so "Build first" can rebuild the solution (the exe itself included)
# while the window is open.
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# already open: bring that window forward (its files are in use - copying over them failed)
$open = Get-Process AI_FGF.TestRunner -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($open) {
    Add-Type -Namespace Win32 -Name Window -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h); [DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);'
    [void][Win32.Window]::ShowWindow($open.MainWindowHandle, 9)   # restore if minimised
    [void][Win32.Window]::SetForegroundWindow($open.MainWindowHandle)
    Write-Host "The test window is already open (process $($open.Id)): brought to the front."
    exit 0
}
dotnet build (Join-Path $root "tools/AI_FGF.TestRunner/AI_FGF.TestRunner.csproj") -c Debug -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "could not build tools/AI_FGF.TestRunner" }
$bin = Join-Path $root "tools/AI_FGF.TestRunner/bin/Debug/net11.0-windows"
$copy = Join-Path $root "artifacts/test-runner"
New-Item -ItemType Directory -Force -Path $copy | Out-Null
Copy-Item -Path (Join-Path $bin "*") -Destination $copy -Recurse -Force
Start-Process -FilePath (Join-Path $copy "AI_FGF.TestRunner.exe")
