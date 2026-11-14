using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AI_FGF;

/// <summary>How a run ended, after applying the <see cref="ExitCodePolicy"/>.</summary>
public enum RunOutcome { Success, Warning, Failure, Cancelled, TimedOut }

/// <summary>
/// Maps a tool's exit codes to <see cref="RunOutcome"/>s with messages — robocopy treats 0–7 as
/// success, msiexec returns 3010 for "reboot required", fc/findstr return 1 for "differences".
/// Default (no rules): 0 = success, anything else = failure.
/// <code>
/// runner.ExitCodes(p => p.Success(0, 7).Warning(3010, "Reboot required").Failure(16, "Fatal robocopy error"));
/// </code>
/// </summary>
public sealed class ExitCodePolicy
{
    private readonly List<(int From, int To)> _success = new();
    private readonly Dictionary<int, string> _warnings = new();
    private readonly Dictionary<int, string> _failures = new();

    /// <summary>Exit codes <paramref name="from"/>..<paramref name="to"/> (inclusive) mean success.</summary>
    public ExitCodePolicy Success(int from, int to)
    {
        if (from > to) throw new ArgumentException($"Range {from}-{to} is empty.", nameof(from));
        _success.Add((from, to));
        return this;
    }

    /// <summary>These exit codes mean success.</summary>
    public ExitCodePolicy Success(params int[] codes)
    {
        foreach (int c in codes) _success.Add((c, c));
        return this;
    }

    /// <summary>This exit code means "finished, but look at this" (counts as <see cref="ProcessResult.Success"/>).</summary>
    public ExitCodePolicy Warning(int code, string message) { _warnings[code] = message; return this; }

    /// <summary>A readable message for a failing exit code.</summary>
    public ExitCodePolicy Failure(int code, string message) { _failures[code] = message; return this; }

    /// <summary>The outcome and message for a process that exited with <paramref name="exitCode"/>.</summary>
    public (RunOutcome Outcome, string Message) Classify(int exitCode)
    {
        if (_warnings.TryGetValue(exitCode, out string? w)) return (RunOutcome.Warning, w);
        if (_failures.TryGetValue(exitCode, out string? f)) return (RunOutcome.Failure, $"exit code {exitCode}: {f}");
        bool ok = _success.Count == 0 ? exitCode == 0 : _success.Any(r => exitCode >= r.From && exitCode <= r.To);
        return ok ? (RunOutcome.Success, "") : (RunOutcome.Failure, $"exit code {exitCode}");
    }

    /// <summary>Parse <c>"0"</c>, <c>"0,1"</c>, <c>"0-7"</c>, <c>"0-3, 8, 16-20"</c> into success ranges.</summary>
    public static bool TryParseRanges(string text, out List<(int From, int To)> ranges, out string error)
    {
        ranges = new(); error = "";
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int dash = part.IndexOf('-', 1);   // a leading '-' is a negative number
            string a = dash > 0 ? part[..dash].Trim() : part, b = dash > 0 ? part[(dash + 1)..].Trim() : part;
            if (!int.TryParse(a, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int from)
                || !int.TryParse(b, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int to))
            { error = $"'{part}' is not a code or a range like 0-7"; return false; }
            if (from > to) { error = $"range '{part}' is empty"; return false; }
            ranges.Add((from, to));
        }
        if (ranges.Count == 0) { error = "is empty"; return false; }
        return true;
    }
}
