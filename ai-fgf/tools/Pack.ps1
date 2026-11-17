# Packs AI_FGF.Core, AI_FGF.Direct3D (the face renderer AI_FGF depends on), AI_FGF (WinForms) and AI_FGF.Avalonia as
# NuGet packages into artifacts/packages.
# An app outside this repository then depends on a pinned release instead of on these sources:
#   nuget.config:  <add key="ai_fgf" value="C:\path\to\code-frameworks\artifacts\packages" />
#   app .csproj:   <PackageReference Include="AI_FGF" Version="0.1.0" />
# Usage: pwsh tools/Pack.ps1 [-Version 0.2.0] [-Output dir]
param(
    [string]$Version = "",
    [string]$Output = (Join-Path $PSScriptRoot "..\artifacts\packages")
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$extra = @()
if ($Version) { $extra += "-p:Version=$Version" }
foreach ($project in "src/AI_FGF.Core", "src/AI_FGF.Direct3D", "src/AI_FGF", "src/AI_FGF.Avalonia") {
    dotnet pack (Join-Path $root $project) -c Release -o $Output --nologo @extra
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack $project failed" }
}
Get-ChildItem $Output -Filter *.nupkg | Sort-Object LastWriteTime | Select-Object -Last 4 | ForEach-Object { "packed    $($_.FullName)" }
