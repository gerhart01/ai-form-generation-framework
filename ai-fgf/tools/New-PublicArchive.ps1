# Builds the zip archive of AI_FGF that can be published on the Internet, and checks it.
#
#   pwsh ai-fgf/tools/New-PublicArchive.ps1 [-Output dir] [-AllowDirty] [-SkipBuild]
#
# The repository: AGENT_GUIDE.md (the instructions, read first) and ai-fgf/ (the framework) are published;
# internal/ is not.
#
# The archive is `git archive HEAD`: committed files only, so nothing local or generated (bin/, obj/,
# artifacts/, a local CLAUDE.md) gets in, and every path marked export-ignore in .gitattributes is left
# out (internal/, ai-fgf/tests/, .gitattributes). The test projects are also removed from AI_FGF.slnx inside the zip.
# The script then opens the zip and fails if
#   - AGENT_GUIDE.md or ai-fgf/AI_FGF.slnx is missing; any entry is under internal/ or ai-fgf/tests/, or is a CLAUDE.md;
#   - any entry is a binary or build output: under bin/, obj/, artifacts/, packages/, TestResults/, .vs/,
#     with a binary extension (.dll, .exe, .pdb, .nupkg, .zip, ...), or with NUL bytes in it (any other binary);
#   - any text file contains a line of internal/publish-denylist.txt (if that file exists: it lives in
#     internal/ because the words themselves must not be published);
#   - the extracted archive does not build (`dotnet build ai-fgf/AI_FGF.slnx`, 0 errors; skip with -SkipBuild).
# Output: ai-fgf/artifacts/publish/AI_FGF-<version>-<yyyyMMdd>-<commit>.zip. Exit code 0 = ready to publish.
param(
    [string]$Output = (Join-Path $PSScriptRoot "..\artifacts\publish"),
    # build the archive even with uncommitted changes (they are NOT in it: it is made from HEAD)
    [switch]$AllowDirty,
    # do not check that the extracted archive builds
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = (git -C $PSScriptRoot rev-parse --show-toplevel).Trim()   # the repository: AGENT_GUIDE.md, ai-fgf/, internal/
$framework = Join-Path $root "ai-fgf"
Push-Location $root
try {
    $dirty = git status --porcelain
    if ($dirty -and -not $AllowDirty) {
        Write-Host "FAIL  uncommitted changes - the archive is made from HEAD and would not contain them:"
        $dirty | ForEach-Object { Write-Host "      $_" }
        Write-Host "      commit them first, or pass -AllowDirty to archive HEAD anyway."
        exit 1
    }

    $commit = (git rev-parse --short HEAD).Trim()
    [xml]$csproj = Get-Content (Join-Path $framework "src/AI_FGF/AI_FGF.csproj")
    $version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    if (-not $version) { $version = "0.0" }
    $name = "AI_FGF-$version-$(Get-Date -Format yyyyMMdd)-$commit"

    New-Item -ItemType Directory -Force -Path $Output | Out-Null
    $zip = Join-Path (Resolve-Path $Output).Path "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    git archive --format=zip --prefix="$name/" -o $zip HEAD
    if ($LASTEXITCODE -ne 0) { throw "git archive failed" }

    # ---- the solution inside the archive must not list the test projects it does not contain
    $archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.GetEntry("$name/ai-fgf/AI_FGF.slnx")
        if ($entry) {
            $reader = New-Object System.IO.StreamReader($entry.Open())
            try { $slnx = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $slnx = [regex]::Replace($slnx, '(?s)[ \t]*<Folder Name="/tests/">.*?</Folder>\r?\n', '')
            $entry.Delete()
            $writer = New-Object System.IO.StreamWriter($archive.CreateEntry("$name/ai-fgf/AI_FGF.slnx").Open(), (New-Object System.Text.UTF8Encoding($false)))
            try { $writer.Write($slnx) } finally { $writer.Dispose() }
        }
    } finally { $archive.Dispose() }

    # ---- check what is inside
    $problems = New-Object System.Collections.Generic.List[string]
    $deny = @()
    $denyFile = Join-Path $root "internal/publish-denylist.txt"
    if (Test-Path $denyFile) {
        $deny = Get-Content $denyFile -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith("#") }
    } else {
        Write-Host "note  internal/publish-denylist.txt not found - only the structure is checked"
    }
    $binaryExt = @(".dll", ".exe", ".pdb", ".nupkg", ".snupkg", ".zip", ".7z", ".rar", ".so", ".dylib", ".lib", ".obj", ".o", ".a",
                   ".msi", ".cache", ".ilk", ".exp", ".pch", ".winmd", ".resources", ".bin")
    $textExt = @(".cs", ".md", ".json", ".csproj", ".props", ".targets", ".slnx", ".ps1", ".txt", ".xml", ".axaml", ".yml", ".yaml", ".gitignore", "")
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    $files = 0
    $seen = New-Object System.Collections.Generic.HashSet[string]
    try {
        foreach ($e in $archive.Entries) {
            $path = $e.FullName.Substring($name.Length + 1)
            if (-not $path -or $path.EndsWith("/")) { continue }
            $files++
            [void]$seen.Add($path)
            if ($path -match '^(internal/|\.gitattributes$)') { $problems.Add("internal file in the archive: $path") }
            if ($path -match '^ai-fgf/tests/') { $problems.Add("test project in the archive: $path") }
            if ($path -eq 'ai-fgf/AI_FGF.slnx') {
                $reader = New-Object System.IO.StreamReader($e.Open())
                try { if ($reader.ReadToEnd() -match 'tests/') { $problems.Add("AI_FGF.slnx still lists the test projects") } } finally { $reader.Dispose() }
            }
            if ($path -match '(^|/)CLAUDE\.md$') { $problems.Add("agent instructions in the archive: $path") }
            if ($path -match '(^|/)(bin|obj|artifacts|packages|TestResults|\.vs)/') { $problems.Add("build output in the archive: $path") }
            if ($binaryExt -contains [System.IO.Path]::GetExtension($path).ToLowerInvariant()) { $problems.Add("binary file in the archive: $path") }
            else {
                # any other binary: a NUL byte in its first 8 KB (text files have none)
                $stream = $e.Open()
                try {
                    $buffer = New-Object byte[] 8192
                    $read = $stream.Read($buffer, 0, $buffer.Length)
                    if ([Array]::IndexOf($buffer, [byte]0, 0, $read) -ge 0) { $problems.Add("binary content in the archive: $path") }
                } finally { $stream.Dispose() }
            }
            if ($deny.Count -gt 0 -and $textExt -contains [System.IO.Path]::GetExtension($path).ToLowerInvariant()) {
                $reader = New-Object System.IO.StreamReader($e.Open())
                try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
                foreach ($word in $deny) {
                    if ($text.IndexOf($word, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $problems.Add("'$word' in $path") }
                }
            }
        }
    } finally { $archive.Dispose() }
    foreach ($required in "AGENT_GUIDE.md", "ai-fgf/AI_FGF.slnx", "ai-fgf/README.md") {
        if (-not $seen.Contains($required)) { $problems.Add("missing from the archive: $required") }
    }

    # ---- the archive on its own must build: nothing in it may depend on what was left out
    $built = "not checked (-SkipBuild)"
    if (-not $SkipBuild -and $problems.Count -eq 0) {
        $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("aifgf-publish-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
        try {
            [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $tmp)
            $log = & dotnet build (Join-Path $tmp "$name/ai-fgf/AI_FGF.slnx") -c Release -nologo 2>&1
            if ($LASTEXITCODE -ne 0) {
                $problems.Add("the extracted archive does not build:")
                $log | Where-Object { $_ -match ': error ' } | Select-Object -First 10 | ForEach-Object { $problems.Add("    $_") }
            } else {
                $warnings = @($log | Where-Object { $_ -match ': warning ' }).Count
                $built = "builds (Release, $warnings warning line(s))"
            }
        } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    $size = [math]::Round((Get-Item $zip).Length / 1KB)
    Write-Host "Archive   $zip"
    Write-Host "Contents  $files files from commit $commit (version $version), $size KB; denylist: $($deny.Count) entr$(if ($deny.Count -eq 1) {'y'} else {'ies'})"
    Write-Host "Build     $built"
    if ($dirty) { Write-Host "warning   uncommitted changes are NOT in the archive (-AllowDirty)" }
    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "  FAIL  $_" }
        Remove-Item $zip -Force
        Write-Host "RESULT: FAIL - $($problems.Count) problem(s); the archive was deleted."
        exit 1
    }
    Write-Host "RESULT: PASS - ready to publish"
    exit 0
}
finally { Pop-Location }
