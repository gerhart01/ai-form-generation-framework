using System.Drawing;
using System.Windows.Forms;
using AI_FGF;

namespace AI_FGF.TestRunner;

/// <summary>
/// The test run in a window, as a console rather than the tool runner's form: a board of groups (run options, one
/// row per shard with its own progress, the run as a whole) and the log beside it. The view of
/// <see cref="ToolRunController"/> (which runs tools/Run-Tests.ps1); the shards are the <see cref="TestRun"/> model.
/// </summary>
internal sealed class MainForm : Form, IToolRunView
{
    private const string WindowTitle = "AI Forms Generation Framework — tests";

    private readonly ToolRunController _controller;
    private readonly TestRun _run = new();
    private readonly AiFgfLog _log = new() { Height = 400 };
    private readonly ComboBox _framework = AiFgf.Combo(editable: false);
    private readonly CheckBox _build = AiFgf.Check("Build first", @checked: true);
    private readonly CheckBox _e2e = AiFgf.Check("End-to-end tests too");
    // the quick loop: only the groups the changes touch, stopping at the first failure (Run-Tests.ps1 -Changed)
    private readonly CheckBox _changed = AiFgf.Check("Only what changed (stop at the first failure)");
    private readonly Button _start, _cancel, _theme;
    private readonly Label _status = AiFgf.Label("Idle", subtle: true);
    private readonly TableLayoutPanel _table = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 6 };
    private readonly AiFgfProgressBar _overall = new() { Width = 520, Height = 22 };
    private readonly Label _overallText = AiFgf.Label("—");
    private readonly Dictionary<Shard, (Label State, AiFgfProgressBar Bar, Label Checks, Label Time, Label Current)> _rows = new();
    private readonly string _script;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    private DateTime? _runStarted, _runEnded;
    private Dictionary<string, int> _last = new();
    /// <summary>How long each shard took last time, written by Run-Tests.ps1 at the end of every run.</summary>
    private string LastFile => Path.Combine(RepoRoot(), "artifacts", "test-logs", "durations.json");

    public MainForm()
    {
        _controller = new ToolRunController(this);
        _script = Path.Combine(RepoRoot(), "tools", "Run-Tests.ps1");
        _framework.Items.AddRange(new object[] { "both", "net11.0", "net10.0" });
        _framework.SelectedIndex = 0;
        _start = AiFgf.Primary(AiFgf.Button("▶ &Run tests", (_, _) => { _run.Reset(); RebuildTable(); _ = _controller.RunAsync(); }));
        _cancel = AiFgf.Button("■ &Cancel", (_, _) => _controller.Cancel());
        _theme = AiFgf.Button("Theme…", (_, _) => PickTheme());
        _cancel.Enabled = false;

        FormScaffold.For(this).Title(WindowTitle).Size(1280, 800)
            .Board(b => b
                .Wide("Run", AiFgf.Label("Framework"), _framework, _build, _changed, _e2e, _start, _cancel, _theme, _status)
                .Wide("Shards", _table)
                .Wide("The whole run", _overall, _overallText),
                side: _log, sideCaption: "Log")
            .Done();
        AutoMinSize.Guard(this, minBodyWidth: 760, minBodyHeight: 420);
        foreach (Control c in new Control[] { _start, _cancel, _theme }) if (c is AiFgfButton btn) btn.Look = AiFgfButtonStyle.Raised;
        _overall.Look = AiFgfButtonStyle.Raised;
        _last = LoadLast();
        _clock.Tick += (_, _) => Tick();
        RebuildTable();
        _log.Line("Run the tests: LayoutTests in shards one after another, AvaloniaTests alongside. Ctrl+F searches the log.");
    }

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "AI_FGF.slnx"))) return d.FullName;
        throw new InvalidOperationException("AI_FGF.slnx not found above " + AppContext.BaseDirectory);
    }

    private Dictionary<string, int> LoadLast()
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(LastFile)) ?? new(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return new(); }
    }

    /// <summary>Every second while running: the running shards' times, the whole run's time and what is left.</summary>
    private void Tick()
    {
        DateTime now = DateTime.Now;
        foreach (Shard s in _run.Shards.Where(s => s.State == ShardState.Running)) ShowShard(s);
        ShowOverall();
        // "running 3:12" while it runs, "passed in 21:31" after: a bare 21:31 read like the time of day
        if (_runStarted is { } t)
        {
            string took = ToolRunController.FormatDuration((_runEnded ?? now) - t);
            Text = _runEnded is null ? $"{WindowTitle} — running {took}" : $"{WindowTitle} — {(_run.Failed > 0 || _run.Shards.Any(s => s.State == ShardState.Failed) ? "failed" : "passed")} in {took}";
        }
    }

    private void PickTheme()
    {
        if (AiFgfThemeDialog.Show(this, AiFgfTheme.Current.Choice) is { } chosen) AiFgfTheme.Switch(chosen.ToTheme());
        RebuildTable();
    }

    // ------------------------------------------------------------------ the shard table

    private void RebuildTable()
    {
        AiFgfTheme t = AiFgfTheme.Current;
        _table.SuspendLayout();
        _table.Controls.Clear();
        _table.RowStyles.Clear();
        _rows.Clear();
        _table.BackColor = t.App;
        Label Cell(string text, bool subtle = false) => new() { Text = text, AutoSize = true, ForeColor = subtle ? t.Subtle : t.Text, Margin = new Padding(3, 6, 12, 3) };
        foreach (string head in new[] { "Shard", "State", "Progress", "Checks", "Time", "Now" }) _table.Controls.Add(Cell(head, subtle: true));
        if (_run.Shards.Count == 0) _table.Controls.Add(Cell("press Run: the shards appear here", subtle: true));
        foreach (Shard s in _run.Shards)
        {
            var bar = new AiFgfProgressBar { Width = 160, Height = 16, Margin = new Padding(3, 6, 12, 3), Look = AiFgfButtonStyle.Raised };
            var current = new Label { AutoSize = false, Width = 360, Height = 20, AutoEllipsis = true, ForeColor = t.Subtle, Margin = new Padding(3, 6, 3, 3) };
            var row = (State: Cell(""), Bar: bar, Checks: Cell(""), Time: Cell(""), Current: current);
            _table.Controls.Add(Cell(s.Name));
            _table.Controls.Add(row.State);
            _table.Controls.Add(row.Bar);
            _table.Controls.Add(row.Checks);
            _table.Controls.Add(row.Time);
            _table.Controls.Add(row.Current);
            _rows[s] = row;
            ShowShard(s);
        }
        _table.ResumeLayout(performLayout: true);
        ShowOverall();
    }

    private void ShowShard(Shard s)
    {
        if (!_rows.TryGetValue(s, out var r)) return;
        AiFgfTheme t = AiFgfTheme.Current;
        (r.State.Text, r.State.ForeColor) = s.State switch
        {
            ShardState.Running => ("running", t.Accent),
            ShardState.Passed => ("ok", t.LogSuccess),
            ShardState.Failed => ("FAIL", t.LogError),
            _ => ("waiting", t.Subtle),
        };
        r.Bar.Value = (int)Math.Round(100 * s.Fraction);
        r.Checks.Text = s.Total > 0 ? $"{s.Passed}/{s.Total}" : s.Cases > 0 ? $"{s.Cases} cases" : "";
        int elapsed = s.Elapsed(DateTime.Now);
        string last = s.LastSeconds > 0 ? $" / {ToolRunController.FormatDuration(TimeSpan.FromSeconds(s.LastSeconds))}" : "";
        r.Time.Text = elapsed > 0 || s.State == ShardState.Running ? ToolRunController.FormatDuration(TimeSpan.FromSeconds(elapsed)) + (s.State == ShardState.Running ? last : "") : s.LastSeconds > 0 ? "~" + ToolRunController.FormatDuration(TimeSpan.FromSeconds(s.LastSeconds)) : "";
        r.Current.Text = s.Current;
    }

    private void ShowOverall()
    {
        _overall.Value = (int)Math.Round(100 * _run.Overall);
        int done = _run.Shards.Count(s => s.State is ShardState.Passed or ShardState.Failed);
        DateTime now = DateTime.Now;
        string time = _runStarted is { } t ? _runEnded is { } e ? $" · took {ToolRunController.FormatDuration(e - t)}" : $" · {ToolRunController.FormatDuration(now - t)} so far" : "";
        string left = _clock.Enabled && _run.Remaining(now) is { } r ? $" · about {ToolRunController.FormatDuration(r)} left" : "";
        _overallText.Text = _run.Shards.Count == 0 ? "—" : $"{done} of {_run.Shards.Count} shards · {_run.Checks} checks · {_run.Failed} failed{time}{left}";
    }

    // ------------------------------------------------------------------ the view of the run (IToolRunView)

    IReadOnlyList<FieldError> IToolRunView.ValidateInputs()
        => File.Exists(_script) ? Array.Empty<FieldError>() : new[] { new FieldError("script", $"{_script} not found") };

    ProcessRunner? IToolRunView.BuildRun()
    {
        var args = new List<string> { "-Framework", (string)_framework.SelectedItem! };
        if (_build.Checked) args.Add("-Build");
        if (_e2e.Checked) args.Add("-E2E");
        if (_changed.Checked) args.Add("-Changed");
        return ProcessRunner.PowerShell(_script, args).WorkingDirectory(RepoRoot());
    }

    TimeSpan? IToolRunView.RunTimeout => null;
    string IToolRunView.ProgressMarker => "##PROGRESS##";
    bool IToolRunView.IsClosing => IsDisposed || Disposing;

    void IToolRunView.AppendLog(string text, RunLogTone tone)
    {
        switch (tone)
        {
            case RunLogTone.Error: _log.Error(text); break;
            case RunLogTone.Warning: _log.Warn(text); break;
            case RunLogTone.Accent: _log.Append(text, AiFgfTheme.Current.LogAccent); break;
            case RunLogTone.Success: _log.Append(text, AiFgfTheme.Current.LogSuccess); break;
            default: _log.Line(text); break;
        }
    }

    void IToolRunView.SetStatus(string text) => OnUi(() => _status.Text = text);

    void IToolRunView.SetRunning(bool running) => OnUi(() =>
    {
        _start.Enabled = !running;
        _cancel.Enabled = running;
        _framework.Enabled = _build.Enabled = _e2e.Enabled = !running;
        _status.Text = running ? "Running…" : "Idle";
        if (running) { _runStarted = DateTime.Now; _runEnded = null; _clock.Start(); }
        else { _runEnded = DateTime.Now; _clock.Stop(); _last = LoadLast(); Tick(); }   // Run-Tests.ps1 has written this run's durations
    });

    void IToolRunView.ShowProgress(string activity, int percent, string status) => OnUi(() => _status.Text = activity);

    bool IToolRunView.HandleOutputLine(string text)
    {
        if (TestRun.IsModelLine(text))
        {
            OnUi(() =>
            {
                int before = _run.Shards.Count;
                Shard? changed = _run.Feed(text);
                if (_run.Shards.Count != before) _run.ApplyLast(_last);
                if (_run.Shards.Count != before || (changed is not null && !_rows.ContainsKey(changed))) RebuildTable();
                else if (changed is not null) { ShowShard(changed); ShowOverall(); }
            });
            return true;
        }
        if (text.Contains("FAIL", StringComparison.Ordinal)) { _log.Error(text.Trim()); return true; }
        return false;
    }

    void IToolRunView.RunFinished(ProcessResult result) => OnUi(ShowOverall);

    private void OnUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(action); } catch (InvalidOperationException) { } return; }
        action();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controller.Cancel();
        _clock.Dispose();
        base.OnFormClosed(e);
    }
}
