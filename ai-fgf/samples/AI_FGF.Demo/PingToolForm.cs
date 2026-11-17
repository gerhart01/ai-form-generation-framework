using System.Data;
using System.Text.RegularExpressions;
using AI_FGF;

namespace AI_FGF.Demo;

/// <summary>
/// A complete tool-runner window in ~40 lines, built on <see cref="ToolForm"/>.
/// The same file compiles against WinForms (samples/AI_FGF.Demo, Windows) and Avalonia
/// (samples/AI_FGF.Demo.Avalonia, Linux/macOS/Windows) — only the project differs.
/// It runs the system <c>ping</c> command, streams its output into the log, and
/// parses the round-trip times into the Results grid. This is the whole point of the
/// framework: the window chrome, Run/Cancel, progress, logging and grid come for free —
/// the subclass only describes its inputs and how to build the command.
/// </summary>
public sealed class PingToolForm : ToolForm
{
    protected override AiFgfButtonStyle ButtonStyle => AiFgfButtonStyle.Glow;   // flat / raised / glow; the user can change it under "Theme…"

    protected override string RunButtonText => "▶ &Ping";
    protected override bool PresetButtons => true;
    protected override bool ThemeButton => true;   // "Theme…": accent, light/dark, background tint

    public PingToolForm()
    {
        Text = "AI Forms Generation Framework demo — Ping";
        Log.Line("Enter a host and press Ping. Output streams here; reply times land in the Results tab.");
    }

    protected override void BuildParameters(FormScaffold.RowBuilder rows)
    {
        rows.Text("Host:", "e.g. 8.8.8.8 or example.com", value: "127.0.0.1", required: true);
        rows.Number("Count:", min: 1, max: 100, value: 4);
        rows.Checks(("Resolve", AiFgf.Check("Resolve addresses to hostnames (-a)")));   // short, stable key for settings/presets
        // Reject spaces and shell-ish characters before they ever reach the command line.
        rows.Fields.Rule("Host", v => v.Trim().Any(ch => char.IsWhiteSpace(ch) || "&|;<>\"'".Contains(ch))
            ? "Host must be a single name or address." : null);
    }

    // Validation (required host, the Host rule) runs before this is called.
    // (Windows ping: -n count, -a resolves names; Linux/macOS ping: -c count, -n stops resolving.)
    protected override ProcessRunner? BuildRun()
        => new ProcessRunner("ping")
            .Option(OperatingSystem.IsWindows() ? "-n" : "-c", Fields.Int("Count"))
            .ArgIf(OperatingSystem.IsWindows() && Fields.Bool("Resolve"), "-a")
            .ArgIf(!OperatingSystem.IsWindows() && !Fields.Bool("Resolve"), "-n")
            .Arg(Fields.Text("Host"));

    protected override void OnRunFinished(ProcessResult result)
    {
        // Pull "time=12ms" / "time<1ms" (Windows) or "time=0.045 ms" (Linux) into a small table.
        var table = new DataTable();
        table.Columns.Add("#");
        table.Columns.Add("Reply line");
        table.Columns.Add("Time");

        int i = 1;
        var rx = new Regex(@"time[=<](?<t>[\d.]+) ?ms", RegexOptions.IgnoreCase);
        foreach (string line in result.StdOut)
        {
            if (!line.Contains("TTL=", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("bytes=", StringComparison.OrdinalIgnoreCase)) continue;
            Match m = rx.Match(line);
            table.Rows.Add(i++, line.Trim(), m.Success ? m.Groups["t"].Value + " ms" : "—");
        }

        if (table.Rows.Count > 0)
        {
            Results.Show(table);
            ShowResultsTab();
        }
    }
}
