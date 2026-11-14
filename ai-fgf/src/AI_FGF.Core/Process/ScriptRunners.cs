using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AI_FGF;

/// <summary>Ready-made runners for PowerShell and Python scripts.</summary>
public sealed partial class ProcessRunner
{
    /// <summary>
    /// Run a PowerShell script with its arguments: pwsh if it is on PATH (else Windows PowerShell),
    /// no profile, non-interactive, execution policy bypassed. Output is full Unicode — the script
    /// is started through <c>-EncodedCommand</c> that first switches the console output to UTF-8
    /// (redirected pwsh output is otherwise the OEM code page and characters outside it become '?').
    /// Arguments are passed as PowerShell string literals, so spaces and quotes are safe, and the
    /// script's exit code is the process's exit code.
    /// </summary>
    /// <remarks>Script arguments go in <paramref name="args"/>; <see cref="Arg"/> would add pwsh options.</remarks>
    public static ProcessRunner PowerShell(string scriptPath, IEnumerable<string>? args = null, string? executable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        string exe = executable ?? FindOnPath("pwsh") ?? FindOnPath("powershell") ?? "powershell";
        var command = new StringBuilder();
        command.Append("[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); ");
        command.Append("$OutputEncoding = [Console]::OutputEncoding; ");
        command.Append("$global:LASTEXITCODE = 0; ");
        command.Append("& ").Append(Literal(Path.GetFullPath(scriptPath)));
        foreach (string a in args ?? Enumerable.Empty<string>()) command.Append(' ').Append(Literal(a));
        // $? must be read first (any statement resets it); a script that ran 'exit N' leaves
        // $? false *and* $LASTEXITCODE = N — keep N; only an error without a code becomes 1.
        command.Append("; $ok = $?; $code = $LASTEXITCODE; if (-not $ok -and -not $code) { exit 1 }; exit $code");

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command.ToString()));
        return new ProcessRunner(exe)
            .Arg("-NoProfile").Arg("-NonInteractive").Arg("-ExecutionPolicy").Arg("Bypass")
            .Arg("-EncodedCommand").Arg(encoded)
            .Utf8()
            .WithDisplay($"{Path.GetFileName(exe)} {scriptPath} {string.Join(' ', args ?? Enumerable.Empty<string>())}".TrimEnd());
    }

    /// <summary>
    /// Run a Python script: <c>python -u</c> (unbuffered, so progress arrives as it is printed)
    /// with UTF-8 forced on both ends (<c>PYTHONIOENCODING</c>, <c>PYTHONUTF8</c>). Uses python on
    /// PATH, else the py launcher. More script arguments can be added with <see cref="Arg"/>.
    /// </summary>
    public static ProcessRunner Python(string scriptPath, IEnumerable<string>? args = null, string? executable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        string exe = executable ?? FindOnPath("python") ?? FindOnPath("python3") ?? FindOnPath("py") ?? "python3";
        var runner = new ProcessRunner(exe).Arg("-u").Arg(scriptPath)
            .Env("PYTHONIOENCODING", "utf-8").Env("PYTHONUTF8", "1")
            .Utf8();
        foreach (string a in args ?? Enumerable.Empty<string>()) runner.Arg(a);
        return runner;
    }

    /// <summary>A PowerShell single-quoted literal: only ' needs escaping (as '').</summary>
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>The full path of <paramref name="name"/> found on PATH (with PATHEXT), or null.</summary>
    public static string? FindOnPath(string name)
    {
        string[] exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string candidate in new[] { name }.Concat(exts.Select(e => name + e.ToLowerInvariant())))
            {
                try
                {
                    string full = Path.Combine(dir.Trim('"'), candidate);
                    // Windows needs an executable extension (PATHEXT); on Linux/macOS programs have none
                    if (File.Exists(full) && (Path.HasExtension(full) || !OperatingSystem.IsWindows())) return full;
                }
                catch (ArgumentException) { /* malformed PATH entry */ }
            }
        }
        return null;
    }
}
