using System.Drawing;
using System.Windows.Forms;
using AI_FGF;

namespace AI_FGF.Showcase;

/// <summary>
/// The framework's showcase, as one console window (a board of groups with the log beside it):
/// <list type="bullet">
/// <item>Look — the theme dialog and the button style (Flat, Raised, Glow with the magic light), switched live;</item>
/// <item>Fields — sunken Direct3D fields, a 3D spinner, a drop-down, an on/off switch;</item>
/// <item>Run — a real <c>ping</c> through the shared <see cref="ToolRunController"/>: output in the log, a 3D progress bar,
/// the replies as rows (<see cref="PingReplies"/>) on 3D tabs;</item>
/// <item>More forms — a form generated from a <c>tool.json</c> spec, and a dialog;</item>
/// <item>Self-check — the window checks its own layout at 96–192 DPI.</item>
/// </list>
/// The window is the view; the run is the controller's, the replies are the model.
/// </summary>
public sealed class ShowcaseForm : Form, IToolRunView
{
    private readonly ToolRunController _controller;
    private readonly PingReplies _replies = new();
    private readonly AiFgfLog _log = new() { Height = 420 };

    private readonly TextBox _host = AiFgf.Text("host name or address");
    private readonly NumericUpDown _count = AiFgf.Number(1, 20, 6);
    private readonly ComboBox _size = AiFgf.Combo(editable: false);
    private readonly ComboBox _style = AiFgf.Combo(editable: false);
    private readonly CheckBox _resolve = AiFgf.Toggle("Resolve names");
    private readonly Button _ping, _cancel;
    private readonly Label _status = AiFgf.Label("Idle", subtle: true);
    private readonly AiFgfProgressBar _progress = new() { Width = 520, Height = 22 };
    private readonly Label _progressText = AiFgf.Label("—", subtle: true);
    private readonly AiFgfTabControl _tabs = new() { Height = 250 };
    private readonly AiFgfDataGridPanel _grid = new() { Dock = DockStyle.Fill };

    public ShowcaseForm()
    {
        _controller = new ToolRunController(this);
        _host.Text = "127.0.0.1";
        _host.Width = 200;
        _count.Width = 70;
        _size.Items.AddRange(new object[] { "32 bytes", "512 bytes", "1400 bytes" });
        _size.SelectedIndex = 0;
        _size.Width = 120;
        _style.Items.AddRange(new object[] { "Flat", "Raised", "Glow" });
        _style.SelectedIndex = (int)AiFgfEffects.Resolve(AiFgfEffects.DefaultButtons);
        _style.Width = 120;
        _style.SelectedIndexChanged += (_, _) => AiFgfEffects.UserButtons = (AiFgfButtonStyle)_style.SelectedIndex;   // every window, live

        _ping = AiFgf.Primary(AiFgf.Button("▶ &Ping", (_, _) => { _replies.Clear(); _grid.Show(_replies.Table); _ = _controller.RunAsync(); }));
        _cancel = AiFgf.Button("■ &Cancel", (_, _) => _controller.Cancel());
        _cancel.Enabled = false;

        var replies = new TabPage("Replies") { BackColor = AiFgfTheme.Current.App };
        replies.Controls.Add(_grid);
        var about = new TabPage("About") { BackColor = AiFgfTheme.Current.App };
        about.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, Padding = new Padding(8), ForeColor = AiFgfTheme.Current.Text,
            Text = "Everything here is the framework's own: the layout (a board of groups), the themes, the Direct3D " +
                   "faces with their flat fallback, the run controller shared with every tool form, and the checker " +
                   "that holds the layout at every DPI. Switch the theme or the button style and watch it all follow.",
        });
        _tabs.TabPages.Add(replies);
        _tabs.TabPages.Add(about);

        FormScaffold.For(this).Title("AI Forms Generation Framework — showcase").Size(1320, 860)
            .Board(b => b
                .Wide("Look", AiFgf.Button("Theme…", (_, _) => PickTheme()), AiFgf.Label("Buttons"), _style,
                      AiFgf.Label("the glow is magic light on .NET 11", subtle: true))
                .Pair("Fields", [AiFgf.Label("Host"), _host, AiFgf.Label("Count"), _count, AiFgf.Label("Size"), _size, _resolve],
                      "Run", [_ping, _cancel, _status])
                .Wide("Progress", _progress, _progressText)
                .Wide("Results", _tabs)
                .Pair("More forms", [AiFgf.Button("A form from tool.json…", (_, _) => OpenSpecForm()), AiFgf.Button("A dialog…", (_, _) => AskDialog())],
                      "Self-check", [AiFgf.Button("Check this window at 96–192 DPI", (_, _) => SelfCheck())]),
                side: _log, sideCaption: "Log")
            .Done();
        AutoMinSize.Guard(this, minBodyWidth: 760, minBodyHeight: 400);   // the board scrolls: it need not be tall

        // the sunken Direct3D card around the text fields (after they are in the window: Wrap takes their place)
        AiFgfFieldGlow.Wrap(_host).Sunken = true;
        ApplyLook();
        AiFgfEffects.Changed += OnEffectsChanged;
        _grid.Show(_replies.Table);
        _log.Line("The showcase: change the look, ping a host, open a generated form, or let the window check itself.");
    }

    // ------------------------------------------------------------------ look

    private void OnEffectsChanged(object? sender, EventArgs e) { if (!IsDisposed) BeginInvoke(ApplyLook); }

    /// <summary>The button style on every Direct3D-capable control of the window.</summary>
    private void ApplyLook()
    {
        AiFgfButtonStyle look = AiFgfEffects.Resolve(AiFgfEffects.DefaultButtons);
        if (_style.SelectedIndex != (int)look) _style.SelectedIndex = (int)look;
        AiFgf.ApplyLook(this, look);
    }

    private void PickTheme()
    {
        if (AiFgfThemeDialog.Show(this, AiFgfTheme.Current.Choice) is { } chosen) AiFgfTheme.Switch(chosen.ToTheme());
    }

    // ------------------------------------------------------------------ more forms

    private void OpenSpecForm()
    {
        string? spec = FindUp(Path.Combine("samples", "AI_FGF.Template", "tool.json"));
        if (spec is null) { _log.Warn("samples/AI_FGF.Template/tool.json not found next to the showcase"); return; }
        var form = new SpecToolForm(ToolSpec.FromFile(spec));
        form.Show(this);
        _log.Success($"Opened the form generated from {spec}");
    }

    private void AskDialog()
    {
        var values = AiFgfDialog.Ask(this, "Add a host", rows => rows
            .Text("Host:", required: true)
            .Number("Port:", min: 1, max: 65535, value: 443)
            .Checks(("Tls", AiFgf.Check("Use TLS", true))), okText: "&Add");
        _log.Line(values is null ? "Dialog cancelled." : "Dialog: " + string.Join(", ", values.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    private static string? FindUp(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        return null;
    }

    // ------------------------------------------------------------------ self-check

    private void SelfCheck()
    {
        _log.Line("Checking a fresh copy of this window at 96, 120, 144 and 192 DPI…");
        var issues = AiFgfConformance.CheckAcrossDpi(() => new ShowcaseForm(), 96, 120, 144, 192);
        if (issues.Count == 0) _log.Success("No layout issues at any DPI: nothing overlaps, nothing is cut off, all text is readable.");
        else foreach (var i in issues.Take(20)) _log.Error(i.ToString());
    }

    // ------------------------------------------------------------------ the view of the run (IToolRunView)

    IReadOnlyList<FieldError> IToolRunView.ValidateInputs()
        => string.IsNullOrWhiteSpace(_host.Text) ? new[] { new FieldError("Host", "Host is required.") } : Array.Empty<FieldError>();

    ProcessRunner? IToolRunView.BuildRun()
    {
        string size = ((string)_size.SelectedItem!).Split(' ')[0];
        return new ProcessRunner("ping").Arg("-n").Arg(((int)_count.Value).ToString(System.Globalization.CultureInfo.InvariantCulture))
                                        .Arg("-l").Arg(size).ArgIf(_resolve.Checked, "-a").Arg(_host.Text.Trim());
    }

    TimeSpan? IToolRunView.RunTimeout => TimeSpan.FromMinutes(2);
    string IToolRunView.ProgressMarker => "##PROGRESS##";
    bool IToolRunView.IsClosing => IsDisposed || Disposing;

    void IToolRunView.AppendLog(string text, RunLogTone tone)
    {
        switch (tone)
        {
            case RunLogTone.Error: _log.Error(text); break;
            case RunLogTone.Warning: _log.Warn(text); break;
            case RunLogTone.Accent: _log.Append(text, AiFgfTheme.Current.LogAccent); break;
            case RunLogTone.Success: _log.Success(text); break;
            default: _log.Line(text); break;
        }
    }

    void IToolRunView.SetStatus(string text) => OnUi(() => _status.Text = text);

    void IToolRunView.SetRunning(bool running) => OnUi(() =>
    {
        _ping.Enabled = !running;
        _cancel.Enabled = running;
        _status.Text = running ? "Pinging…" : "Idle";
    });

    void IToolRunView.ShowProgress(string activity, int percent, string status) => OnUi(() =>
    {
        _progress.Value = Math.Clamp(percent, 0, 100);
        _progressText.Text = string.IsNullOrEmpty(status) ? activity : $"{activity} — {status}";
    });

    /// <summary>A reply becomes a row and moves the progress bar; the line still goes to the log.</summary>
    bool IToolRunView.HandleOutputLine(string text)
    {
        OnUi(() =>
        {
            if (!_replies.Add(text)) return;
            int total = (int)_count.Value;
            _progress.Value = Math.Clamp(100 * _replies.Count / Math.Max(1, total), 0, 100);
            _progressText.Text = $"{_replies.Count} of {total} replies";
        });
        return false;
    }

    void IToolRunView.RunFinished(ProcessResult result) => OnUi(() => _tabs.SelectedIndex = 0);

    private void OnUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(action); } catch (InvalidOperationException) { } return; }
        action();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controller.Cancel();
        AiFgfEffects.Changed -= OnEffectsChanged;
        base.OnFormClosed(e);
    }
}
