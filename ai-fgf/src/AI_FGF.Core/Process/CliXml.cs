using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AI_FGF;

/// <summary>How a rendered log line should be coloured.</summary>
public enum LogSeverity { Normal, Warning, Error }

/// <summary>One line to write to the log, with its severity.</summary>
public readonly record struct LogLine(string Text, LogSeverity Severity);

/// <summary>
/// Turns PowerShell CLIXML output back into readable text.
///
/// A <c>pwsh</c> child whose stdout is read by another <c>pwsh</c> (the "minishell"), or one
/// launched with <c>-OutputFormat Xml</c>, does not print plain text: it serialises every record
/// as a <c>#&lt; CLIXML</c> header line followed by one or more
/// <c>&lt;Objs&gt;…&lt;/Objs&gt;</c> documents. A tool runner that just logs the stream verbatim
/// then shows a wall of XML instead of the tool's output. This converts it back:
/// <list type="bullet">
/// <item>stdout — what the host shows comes through the <b>Output</b> stream as <c>&lt;S S="Output"&gt;</c>
/// fragments: <c>Write-Host</c> text followed by a <c>_x000D__x000A_</c> newline fragment, and
/// pipeline objects one after another (each its own line).</item>
/// <item>stderr — <c>&lt;Obj S="Error"&gt;</c> error records (their direct <c>&lt;ToString&gt;</c>
/// is the message), <c>&lt;S S="warning"&gt;</c> warnings, coloured accordingly, and verbose/debug text.</item>
/// <item><c>&lt;Obj S="information"&gt;</c> records are dropped: they repeat every Write-Host and
/// Write-Information message (the silenced ones too) whose shown text is already on stdout.</item>
/// </list>
/// Checked against real <c>powershell -OutputFormat Xml</c> (5.1) and <c>pwsh</c> (7) output.
/// Progress records are dropped. Anything that is not CLIXML passes through untouched, so feeding
/// an ordinary tool's output through this is a no-op.
/// </summary>
public static class CliXml
{
    /// <summary>The header PowerShell writes before each CLIXML batch.</summary>
    public const string Sentinel = "#< CLIXML";

    // _xHHHH_ escapes a UTF-16 code unit that is not a legal (or is an ambiguous) XML character —
    // notably _x000D__x000A_ for a CR/LF. (The encoder escapes a literal leading underscore as
    // _x005F_ so this never mis-decodes real text.)
    private static readonly Regex HexEscape = new(@"_x([0-9A-Fa-f]{4})_", RegexOptions.Compiled);

    private static string Decode(string s)
        => s.IndexOf("_x", StringComparison.Ordinal) < 0
            ? s
            : HexEscape.Replace(s, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

    /// <summary>
    /// Render one or more concatenated <c>&lt;Objs&gt;</c> documents to log lines, or <c>null</c>
    /// if <paramref name="xml"/> is not well-formed yet (the caller is still collecting it).
    /// </summary>
    public static IReadOnlyList<LogLine>? Render(string xml)
    {
        XElement root;
        try
        {
            // Wrap so several sibling <Objs> documents parse as one fragment. The documents carry
            // their own xmlns, so we match every element by local name and ignore the namespace.
            root = XElement.Parse("<AiFgfCliXml>" + xml + "</AiFgfCliXml>", LoadOptions.PreserveWhitespace);
        }
        catch (XmlException) { return null; }

        var lines = new List<LogLine>();
        var outBuf = new StringBuilder();   // Output-stream fragments carry their own newlines

        void FlushOutput()
        {
            if (outBuf.Length == 0) return;
            AddSplit(lines, outBuf.ToString(), LogSeverity.Normal);
            outBuf.Clear();
        }

        foreach (XElement objs in root.Elements().Where(e => e.Name.LocalName == "Objs"))
        {
            foreach (XElement child in objs.Elements())
            {
                string stream = ((string?)child.Attribute("S") ?? "").ToLowerInvariant();
                if (stream == "progress") continue;                 // progress-bar noise, not output

                if (stream is "output" or "")
                {
                    // Write-Host text is followed by its own newline fragment; pipeline objects ('a', 'b', 42)
                    // come one after another with none, and each is a line of its own. (So Write-Host -NoNewline
                    // followed by more text splits into two lines: the fragments cannot tell it apart.)
                    string text = Decode(TextOf(child));
                    bool newline = text is "\n" or "\r\n" or "\r";
                    if (!newline && outBuf.Length > 0 && outBuf[^1] != '\n' && outBuf[^1] != '\r') outBuf.Append('\n');
                    outBuf.Append(text);
                    continue;
                }

                // Information records are a copy of every Write-Host / Write-Information message, the
                // silenced ones too; what the host actually showed is already in the Output stream.
                if (stream == "information") continue;

                FlushOutput();                                      // keep ordering across streams
                LogSeverity sev = stream switch
                {
                    "error" => LogSeverity.Error,
                    "warning" => LogSeverity.Warning,
                    _ => LogSeverity.Normal,                        // information, verbose, debug, host
                };
                AddSplit(lines, Decode(TextOf(child)), sev);        // a record may itself be multi-line
            }
        }
        FlushOutput();
        return lines;
    }

    /// <summary>The readable text of a serialised node: an &lt;Obj&gt;'s direct &lt;ToString&gt;, else the value.</summary>
    private static string TextOf(XElement e)
        => e.Name.LocalName == "Obj"
            ? e.Elements().FirstOrDefault(c => c.Name.LocalName == "ToString")?.Value ?? ""
            : e.Value;

    private static void AddSplit(List<LogLine> into, string text, LogSeverity sev)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        int end = text.Length;
        if (end > 0 && text[end - 1] == '\n') end--;                // a single trailing newline is the line end, not a blank line
        int start = 0;
        for (int i = 0; i <= end; i++)
        {
            if (i == end || text[i] == '\n')
            {
                into.Add(new LogLine(text.Substring(start, i - start), sev));
                start = i + 1;
            }
        }
    }
}

/// <summary>
/// A stateful, thread-safe filter a tool runner feeds its raw output lines through: CLIXML batches
/// (see <see cref="CliXml"/>) are collected and converted to readable lines; every other line
/// passes through unchanged. stdout and stderr are collected apart: a real PowerShell writes a batch
/// to each at the same time, and their lines interleave. Create one per run (or call
/// <see cref="Reset"/> between runs).
/// </summary>
public sealed class CliXmlLogFilter
{
    private const int MaxBufferChars = 16 * 1024 * 1024;   // give up collecting past this and emit raw
    private static readonly IReadOnlyList<LogLine> None = Array.Empty<LogLine>();

    private readonly object _gate = new();
    private readonly Collector _stdout = new(OutputKind.StdOut), _stderr = new(OutputKind.StdErr);

    /// <summary>Feed one raw output line; returns the line(s) to log (none while a batch is still arriving).</summary>
    public IReadOnlyList<LogLine> Feed(OutputKind kind, string line)
    {
        lock (_gate) return (kind == OutputKind.StdErr ? _stderr : _stdout).Feed(line);
    }

    /// <summary>Emit anything still being collected (as readable text if it parses, else raw), stdout first. Call when the run ends.</summary>
    public IReadOnlyList<LogLine> Flush()
    {
        lock (_gate)
        {
            var a = _stdout.Flush();
            var b = _stderr.Flush();
            return a.Count == 0 ? b : b.Count == 0 ? a : a.Concat(b).ToList();
        }
    }

    /// <summary>Discard any half-collected batch (start a fresh run).</summary>
    public void Reset() { lock (_gate) { _stdout.Reset(); _stderr.Reset(); } }

    /// <summary>One stream's batch being collected.</summary>
    private sealed class Collector
    {
        private readonly OutputKind _kind;
        private readonly StringBuilder _buf = new();
        private readonly List<string> _raw = new();            // the exact lines collected, for a raw fallback
        private bool _collecting;
        private bool _sawObjsStart;

        public Collector(OutputKind kind) => _kind = kind;

        public IReadOnlyList<LogLine> Feed(string line)
        {
            if (!_collecting)
            {
                if (IsSentinel(line)) { Begin(sawObjs: false); return None; }
                if (LooksLikeObjs(line)) { Begin(sawObjs: true); return Collect(line); }
                return Pass(line);
            }
            if (IsSentinel(line)) return None;                 // a second header before the documents: ignore
            return Collect(line);
        }

        public IReadOnlyList<LogLine> Flush()
        {
            if (!_collecting) return None;
            IReadOnlyList<LogLine> result = (_sawObjsStart ? CliXml.Render(_buf.ToString()) : null) ?? Raw();
            Reset();
            return result;
        }

        public void Reset()
        {
            _collecting = false;
            _sawObjsStart = false;
            _buf.Clear();
            _raw.Clear();
        }

        private void Begin(bool sawObjs)
        {
            _collecting = true;
            _sawObjsStart = sawObjs;
            _buf.Clear();
            _raw.Clear();
        }

        private IReadOnlyList<LogLine> Collect(string line)
        {
            _raw.Add(line);
            if (_buf.Length > 0) _buf.Append('\n');
            _buf.Append(line);

            if (!_sawObjsStart)
            {
                string lead = _buf.ToString().TrimStart();
                if (lead.Length == 0) return None;             // only blank lines so far — keep waiting
                if (!lead.StartsWith("<Objs", StringComparison.Ordinal))
                    return Raw();                              // the header was not CLIXML after all: emit raw
                _sawObjsStart = true;
            }

            if (EndsWithObjsClose(_buf) && CliXml.Render(_buf.ToString()) is { } rendered)
            {
                Reset();
                return rendered;
            }
            return _buf.Length > MaxBufferChars ? Raw() : None;
        }

        // What we swallowed turned out not to be CLIXML — re-emit it verbatim so nothing is lost.
        private IReadOnlyList<LogLine> Raw()
        {
            var lines = new List<LogLine>(_raw.Count + 1);
            if (!_sawObjsStart) lines.Add(new LogLine(CliXml.Sentinel, Severity)); // the consumed header
            foreach (string l in _raw) lines.Add(new LogLine(l, Severity));
            Reset();
            return lines;
        }

        private LogSeverity Severity => _kind == OutputKind.StdErr ? LogSeverity.Error : LogSeverity.Normal;

        private IReadOnlyList<LogLine> Pass(string line) => new[] { new LogLine(line, Severity) };
    }

    private static bool IsSentinel(string line)
        => line.AsSpan().Trim().SequenceEqual(CliXml.Sentinel);

    private static bool LooksLikeObjs(string line)
    {
        ReadOnlySpan<char> s = line.AsSpan().TrimStart();
        return s.StartsWith("<Objs") && line.Contains("schemas.microsoft.com/powershell", StringComparison.Ordinal);
    }

    private static bool EndsWithObjsClose(StringBuilder sb)
    {
        const string close = "</Objs>";
        int i = sb.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(sb[i])) i--;
        if (i + 1 < close.Length) return false;
        for (int j = close.Length - 1; j >= 0; j--)
            if (sb[i--] != close[j]) return false;
        return true;
    }
}
