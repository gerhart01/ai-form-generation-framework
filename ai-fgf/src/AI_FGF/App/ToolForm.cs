using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// A ready-made "tool runner" window — the shape hand-coded tool GUIs
/// converge on: a parameters section on top, a Run/Cancel bar with a
/// progress strip, and a tabbed body with a live <b>Output</b> log and a <b>Results</b> grid.
///
/// Subclass it and implement two methods:
/// <list type="bullet">
/// <item><see cref="BuildParameters"/> — add input rows and keep field references.</item>
/// <item><see cref="BuildRun"/> — turn those inputs into a <see cref="ProcessRunner"/>
/// (return <c>null</c> after showing a message to abort the run).</item>
/// </list>
/// Everything else — button enabling, cancellation, cross-thread log streaming, progress
/// parsing, the status bar — is handled here. Emit progress from your tool as
/// <c>##PROGRESS##|activity|percent|status</c> lines on stdout and the bar follows.
/// </summary>
public abstract class ToolForm : Form, IToolRunView
{
    private readonly AiFgfLog _log = new();
    private readonly AiFgfDataGridPanel _results = new();
    private readonly ProgressBar _progress = new AiFgfProgressBar { Dock = DockStyle.Bottom, Height = 18, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
    private readonly Label _progressLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = AiFgfText.Idle, AutoEllipsis = true };
    private readonly ToolStripStatusLabel _status = new(AiFgfText.Idle);
    private readonly TabControl _tabs = new AiFgfTabControl { Dock = DockStyle.Fill };

    private Button _runButton = null!;
    private Button _cancelButton = null!;
    private readonly FieldSet _fields;
    private readonly Dictionary<string, ComboBox> _markerCombos = new(StringComparer.Ordinal);
    private AiFgfBusyOverlay? _busy;
    private volatile bool _running;   // what the view shows (SetRunning); the controller owns the run itself
    private volatile bool _closing;
    // the controller: validate -> build -> run -> report, shared with the Avalonia ToolForm (this form is its view)
    private readonly ToolRunController _controller;

    /// <summary>The output log, for appending from <see cref="BuildRun"/> or <see cref="OnRunFinished"/>.</summary>
    protected AiFgfLog Log => _log;

    /// <summary>
    /// All parameter inputs by key. Mark fields <c>required</c> when adding rows, or attach rules
    /// (<c>Fields.Rule("Port", v => …)</c>); the run is blocked until every field validates.
    /// </summary>
    protected FieldSet Fields => _fields;

    /// <summary>
    /// Key under which window placement and field values are saved between sessions
    /// (<c>%AppData%\AI_FGF\{key}.json</c>). Return null to disable persistence.
    /// </summary>
    protected virtual string? SettingsKey => GetType().FullName;

    /// <summary>Where settings are stored; override to relocate (e.g. portable mode, tests).</summary>
    /// <summary>
    /// Off: no tool window reads or writes settings files (tests, kiosk/portable use). Default on.
    /// </summary>
    public static bool PersistSettings { get; set; } = true;

    protected virtual string? SettingsPath => PersistSettings && SettingsKey is { } key ? AiFgfSettings.DefaultPath(key) : null;

    /// <summary>
    /// Stop a run that takes longer than this (the process tree is killed and the run reported
    /// as timed out). Null = no limit. A runner's own <see cref="ProcessRunner.Timeout"/> wins.
    /// </summary>
    protected virtual TimeSpan? RunTimeout => null;

    /// <summary>Show "Save preset…" / "Load preset…" buttons (JSON files of the field values).</summary>
    protected virtual bool PresetButtons => false;

    /// <summary>
    /// Dim the window with a translucent overlay while a run is in progress. Off by default:
    /// it would also cover the live log — enable it for tools whose output only matters at the end.
    /// </summary>
    protected virtual bool ShowBusyOverlay => false;

    /// <summary>The results grid; call <c>Results.Show(dataTable)</c> or <c>Results.ShowCsv(path)</c>.</summary>
    protected AiFgfDataGridPanel Results => _results;

    // ---- automation / tests: the same public members as the Avalonia ToolForm
    public Button RunButton => _runButton;
    public Button CancelRunButton => _cancelButton;
    public bool IsRunning => _running;
    public FieldSet FieldValues => _fields;
    public AiFgfLog LogView => _log;
    public AiFgfDataGridPanel ResultsView => _results;
    /// <summary>Completes when the run started by <see cref="StartRun()"/> (Run button, Ctrl+Enter, F5) has finished.</summary>
    public Task? CurrentRun { get; private set; }

    /// <summary>What Run, Ctrl+Enter and F5 do: validate and start a run.</summary>
    public void StartRun()
    {
        if (_controller.IsRunning) return;
        CurrentRun = _controller.RunAsync();
    }
#if NET11_0_OR_GREATER

    private Func<ProcessRunner?>? _nextRun;
    private readonly System.Collections.Generic.List<Control> _actionButtons = new();

    /// <summary>
    /// .NET 11: start a run of another command - a second action of the form ("Stop" beside "Start"): validated, logged,
    /// cancelled and timed exactly as the Run button's, by the same controller; <paramref name="build"/> takes the place
    /// of <see cref="BuildRun"/> for this one run.
    /// </summary>
    protected void StartRun(Func<ProcessRunner?> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (_controller.IsRunning) return;
        _nextRun = build;
        CurrentRun = _controller.RunAsync();
    }

    /// <summary>.NET 11: a button of a second action - disabled while any run is going, as the Run button is.</summary>
    protected void AddActionButton(Control button) { if (!_actionButtons.Contains(button)) _actionButtons.Add(button); }
#endif

    /// <summary>The marker prefix your tool prints for progress updates. Default <c>##PROGRESS##</c>.</summary>
    protected string ProgressMarker { get; set; } = "##PROGRESS##";

    /// <summary>Label on the primary action button (an ampersand marks the Alt mnemonic).</summary>
    protected virtual string RunButtonText => AiFgfText.Run;

    /// <summary>Starts a run from anywhere in the window (F5 always does too).</summary>
    protected virtual Keys RunShortcut => Keys.Control | Keys.Enter;

    /// <summary>Cancels a run in progress (ignored while idle, so it never closes or clears anything).</summary>
    protected virtual Keys CancelShortcut => Keys.Escape;

    /// <summary>The window title.</summary>
    protected virtual string FormTitle => "AI Forms Generation Framework tool";

    /// <summary>The Windows 11 backdrop material for this window (no-op pre-Win11). Default Mica.</summary>
    protected virtual BackdropKind Backdrop => BackdropKind.Mica;

    /// <summary>
    /// Show a "Theme…" button that opens <see cref="AiFgfThemeDialog"/> (accent colour, light/dark,
    /// background tint, previewed live). The choice is saved with the window's settings.
    /// </summary>
    protected virtual bool ThemeButton => false;

    /// <summary>
    /// Draw every input field (text, combo, number, memo) as a sunken Direct3D card with a
    /// focus-glow ring — the same effect <see cref="Results"/> gets. Off by default.
    /// </summary>
    protected virtual bool SunkenFields => false;

    /// <summary>
    /// While the tool runs, add each stdout line <c>&lt;marker&gt;|value</c> to <paramref name="combo"/>'s
    /// drop-down (deduplicated) and hide the marker line from the log. Register it when you build the row.
    /// </summary>
    protected void FillComboFromMarker(string marker, ComboBox combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        if (!string.IsNullOrEmpty(marker)) _markerCombos[marker] = combo;
    }

    /// <summary>
    /// This form's button style (<see cref="AiFgfButtonStyle"/>: flat, raised or glowing, the effects drawn with
    /// Direct3D 12 and flat where it does not work). Default: <see cref="AiFgfEffects.DefaultButtons"/>. The user's
    /// pick in the "Theme…" dialog and <c>AI_FGF_BUTTONS</c> win over it (<see cref="AiFgfEffects.Resolve"/>).
    /// </summary>
    protected virtual AiFgfButtonStyle ButtonStyle => AiFgfEffects.DefaultButtons;

    /// <summary>The style the buttons show now.</summary>
    public AiFgfButtonStyle EffectiveButtonStyle => AiFgfEffects.Resolve(ButtonStyle);

    /// <summary>Give every <see cref="AiFgfButton"/> of the form <see cref="EffectiveButtonStyle"/> (also after <see cref="AiFgfEffects.Changed"/>).</summary>
    protected void ApplyButtonStyle() => AiFgf.ApplyLook(this, EffectiveButtonStyle);   // .NET 11: every 3D control, not only the buttons

    private void OnEffectsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(ApplyButtonStyle); else ApplyButtonStyle();
    }

    protected ToolForm()
    {
        _controller = new ToolRunController(this);
        _progressLabel.ForeColor = AiFgfTheme.Current.Text;

        var scaffold = FormScaffold.For(this)
            .Title(FormTitle)
            .Size(1040, 760)
            .Parameters(AiFgfText.Parameters, BuildParameters)
            .Buttons(bar =>
            {
                bar.Primary(RunButtonText, (_, _) => StartRun(), out _runButton);
                bar.Button(AiFgfText.CancelRun, (_, _) => CancelRun(), out _cancelButton);
                _runButton.Name = "Run"; _cancelButton.Name = "Cancel";   // AutomationId: stable across every tool (FlaUI etc.)
                ConfigureButtons(bar);
                if (PresetButtons)
                {
                    bar.Button(AiFgfText.SavePreset, (_, _) => SavePreset(), out _);
                    bar.Button(AiFgfText.LoadPreset, (_, _) => LoadPreset(), out _);
                }
                if (ThemeButton) bar.Button(AiFgfText.ThemeButton, (_, _) => AiFgfThemeDialog.Show(this), out _);
            })
            .Body(BuildBody());

        _fields = scaffold.Fields;
        _cancelButton.Enabled = false;
        scaffold.Done();
        _fields.Refresh();   // apply EnableWhen/VisibleWhen to the initial values
        if (SunkenFields) WrapFieldsSunken();

        // Status strip is docked Bottom directly (scaffold owns Top/Fill).
        var strip = new StatusStrip { BackColor = AiFgfTheme.Current.GridHeader, ForeColor = AiFgfTheme.Current.Text, SizingGrip = false };
        _status.ForeColor = AiFgfTheme.Current.Subtle;
        strip.Items.Add(_status);
        Controls.Add(strip);

        // Guard the minimum size against the button bar (width) and the docked stack (height),
        // so buttons never overlap or clip however the window is resized, at any DPI.
        // The parameter rows stack when narrow and the bar wraps, so the body floor can be modest.
        AutoMinSize.Guard(this, scaffold.ButtonBar, minBodyWidth: 380, minBodyHeight: 220);

        // Modern Windows 11 frame (Mica + theme-matched title bar + rounded corners); no-op elsewhere.
        Dwm.Apply(this, Backdrop);
    }

    /// <summary>Wrap each input field in a sunken <see cref="AiFgfFieldGlow"/> (keeps its grid cell, anchor and margin).</summary>
    private void WrapFieldsSunken()
    {
        foreach (string key in _fields.Keys.ToList())
        {
            Control c = _fields[key];
            // single-line inputs and memos; a check box / radio sits in a flow row and is left flat
            if (c is TextBoxBase or ComboBox or UpDownBase && c.Parent is not null)
                AiFgfFieldGlow.Wrap(c).Sunken = true;
        }
    }

    // ---------------------------------------------------------- persistence

    /// <summary>Keyboard workflow: run, cancel, and jump to the log's find box.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData == RunShortcut || keyData == Keys.F5) && !_running && _runButton.Enabled)
        {
            StartRun();
            return true;
        }
        if (keyData == CancelShortcut && _running)
        {
            CancelRun();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F))
        {
            _tabs.SelectedIndex = 0;
            _log.FocusFind();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        AiFgfCrash.SetLogSource(() => IsDisposed ? Array.Empty<string>() : _log.Lines);   // crash reports include the log tail
        // start in the first field the user can type into, not in the log
        string? first = _fields.Keys.FirstOrDefault(k => _fields.IsActive(k) && _fields[k].Enabled);
        if (first is not null) ActiveControl = _fields[first];
        WindowPlacement? placement = null;
        if (SettingsPath is { } path)
        {
            var data = AiFgfSettings.Load(path);
            placement = data.Window;
            _fields.SetValues(data.Fields);
            if (ThemeButton && AiFgfThemeChoice.TryParse(data.Theme, out AiFgfThemeChoice saved) && !saved.Equals(AiFgfTheme.Current.Choice))
                AiFgfTheme.Switch(saved.ToTheme(), this);
            if (ThemeButton && AiFgfEffects.TryParse(data.Buttons, out AiFgfButtonStyle buttons)) AiFgfEffects.UserButtons = buttons;
        }
        ApplyButtonStyle();
        AiFgfEffects.Changed += OnEffectsChanged;
        if (placement is not null) { AiFgfSettings.Restore(this, placement); return; }
        FitDefaultSizeToScreen();
    }

    /// <summary>
    /// First run (nothing restored): the default size is in logical units, so at 150–200 % it can
    /// be taller than a laptop's working area. Shrink it to fit (never below MinimumSize).
    /// </summary>
    private void FitDefaultSizeToScreen()
    {
        if (WindowState != FormWindowState.Normal) return;
        Rectangle area = Screen.FromControl(this).WorkingArea;
        int w = Math.Max(MinimumSize.Width, Math.Min(Width, area.Width));
        int h = Math.Max(MinimumSize.Height, Math.Min(Height, area.Height));
        if (w == Width && h == Height) return;
        Size = new Size(w, h);
        if (StartPosition == FormStartPosition.CenterScreen)
            Location = new Point(area.X + Math.Max(0, (area.Width - w) / 2), area.Y + Math.Max(0, (area.Height - h) / 2));
    }

    private void SaveSettings()
    {
        if (SettingsPath is not { } path) return;
        try
        {
            AiFgfSettings.Save(path, new FormSettingsData
            {
                Window = AiFgfSettings.Capture(this),
                Fields = _fields.GetPersistableValues(),   // never a password
                Theme = ThemeButton ? AiFgfTheme.Current.Choice?.ToString() : null,
                Buttons = ThemeButton && AiFgfEffects.UserButtons is { } b ? AiFgfEffects.Name(b) : null,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // settings are a convenience — never block closing the window
        }
    }

    private void SavePreset()
    {
        string? path = AiFgf.PickSaveFile(AiFgfText.PresetFilter, AiFgfText.SavePreset.TrimEnd('…'), "preset.json");
        if (path is not null) SavePresetTo(path);
    }

    private void LoadPreset()
    {
        string? path = AiFgf.PickOpenFile(AiFgfText.PresetFilter, AiFgfText.LoadPreset.TrimEnd('…'));
        if (path is not null) LoadPresetFrom(path);
    }

    /// <summary>
    /// Save the field values as a JSON preset. A read-only, locked or unreachable target is
    /// reported in the log (returns false) instead of crashing the click handler.
    /// </summary>
    protected bool SavePresetTo(string path) => ToolPresets.Save(path, _fields, ((IToolRunView)this).AppendLog);

    /// <summary>Apply a JSON preset to the fields; false (with a log line) if it had no values.</summary>
    protected bool LoadPresetFrom(string path) => ToolPresets.Load(path, _fields, ((IToolRunView)this).AppendLog);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _busy?.Dispose();
        base.Dispose(disposing);
    }

    private Control BuildBody()
    {
        // Progress strip sits above the tabs; both go into a host panel used as the fill body.
        var host = new Panel { Dock = DockStyle.Fill };

        // Both composites must fill their tab — without Dock.Fill a UserControl keeps its
        // default 150×150 size and floats in the top-left (invisible to an overlap check,
        // but obvious in a screenshot).
        _log.Dock = DockStyle.Fill;
        _results.Dock = DockStyle.Fill;
        var tabOutput = new TabPage(AiFgfText.Output) { BackColor = AiFgfTheme.Current.App };
        tabOutput.Controls.Add(_log);
        var tabResults = new TabPage(AiFgfText.Results) { BackColor = AiFgfTheme.Current.App };
        tabResults.Controls.Add(_results);
        // the same Direct3D sunken card AiFgfFieldGlow gives a text field, so Results reads as
        // inset into the window instead of floating flush against the tab - and brightens its
        // ring when the grid or its filter box has focus, like every other field in the form.
        AiFgfFieldGlow.Wrap(_results, margin: 8).Sunken = true;
        _tabs.TabPages.Add(tabOutput);
        _tabs.TabPages.Add(tabResults);

        var progressPanel = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 2, 8, 4) };
        // Dock order: the Fill control must be added FIRST (resolved last) so the Bottom
        // bar reserves its edge and the label takes only the space above it. Adding Fill
        // last makes it cover the whole panel and the bar overlaps it.
        progressPanel.Controls.Add(_progressLabel);  // Fill (added first)
        progressPanel.Controls.Add(_progress);       // Bottom (added last)

        host.Controls.Add(_tabs);          // Fill first
        host.Controls.Add(progressPanel);  // Top
        return host;
    }

    // ------------------------------------------------------- subclass surface

    /// <summary>Add parameter rows and keep references to the inputs you need in <see cref="BuildRun"/>.</summary>
    protected abstract void BuildParameters(FormScaffold.RowBuilder rows);

    /// <summary>
    /// Build the process to run from the current inputs. Return <c>null</c> (after showing a
    /// message, if useful) to cancel the launch — e.g. a required field is empty.
    /// </summary>
    protected abstract ProcessRunner? BuildRun();

    /// <summary>Add extra buttons to the bar (optional).</summary>
    protected virtual void ConfigureButtons(FormScaffold.BarBuilder bar) { }

    /// <summary>Called on the UI thread after a run finishes (success, failure or cancel).</summary>
    protected virtual void OnRunFinished(ProcessResult result) { }

    /// <summary>Switch to the Results tab (e.g. from <see cref="OnRunFinished"/> after loading a grid).</summary>
    protected void ShowResultsTab() => _tabs.SelectedTab = _tabs.TabPages[1];

    // --------------------------------------------------------------- the view of the run (IToolRunView)

    IReadOnlyList<FieldError> IToolRunView.ValidateInputs() => _fields.Validate();
#if NET11_0_OR_GREATER
    ProcessRunner? IToolRunView.BuildRun()
    {
        Func<ProcessRunner?>? next = _nextRun;
        _nextRun = null;
        return next is null ? BuildRun() : next();
    }
#else
    ProcessRunner? IToolRunView.BuildRun() => BuildRun();
#endif
    TimeSpan? IToolRunView.RunTimeout => RunTimeout;
    string IToolRunView.ProgressMarker => ProgressMarker;
    bool IToolRunView.IsClosing => _closing;
    void IToolRunView.SetStatus(string text) { if (!_closing && !IsDisposed) RunOnUi(() => _status.Text = text); }
    void IToolRunView.SetRunning(bool running) => SetRunning(running);
    void IToolRunView.ShowProgress(string activity, int percent, string status) => UpdateProgress(activity, percent, status);
    void IToolRunView.RunFinished(ProcessResult result) => OnRunFinished(result);

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

    bool IToolRunView.HandleOutputLine(string text)
    {
        foreach (var (marker, combo) in _markerCombos)
            if (text.StartsWith(marker, StringComparison.Ordinal)) { HandleChoiceMarker(combo, text); return true; }
        return false;
    }

    private void RunOnUi(Action action)
    {
        if (InvokeRequired) { try { BeginInvoke(action); } catch (InvalidOperationException) { } return; }
        action();
    }

    /// <summary>
    /// Tear down only once the close is final. A FormClosing handler (or a failed Validating)
    /// can still cancel the close; doing this in FormClosing used to leave a vetoed window
    /// with its run cancelled and its log and buttons frozen.
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing = true;
        _log.MarkClosing();
        // Cancelling kills the process tree synchronously (ProcessRunner registers the kill on
        // the token), so nothing is orphaned even though the app may exit right after this.
        _controller.Cancel();
        _busy?.HideOverlay();
        SaveSettings();
        AiFgfEffects.Changed -= OnEffectsChanged;
        base.OnFormClosed(e);
    }

    /// <summary>"0.4 s", "12.3 s", "4:05", "1:02:03".</summary>
    public static string FormatDuration(TimeSpan d) => ToolRunController.FormatDuration(d);

    private void CancelRun() => _controller.Cancel();

    private void SetRunning(bool running)
    {
        _running = running;
        if (_closing || IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(() => SetRunning(running)); } catch { } return; }
        _runButton.Enabled = !running;
        _cancelButton.Enabled = running;
#if NET11_0_OR_GREATER
        foreach (Control action in _actionButtons) action.Enabled = !running;
#endif
        _status.Text = running ? AiFgfText.Running : AiFgfText.Idle;
        UseWaitCursor = running;
        if (ShowBusyOverlay)
        {
            if (running)
            {
                // Recreate if it was disposed (e.g. torn down along with a re-created owner).
                if (_busy is null || _busy.IsDisposed) _busy = new AiFgfBusyOverlay();
                _busy.ShowOver(this, AiFgfText.Running);
            }
            else _busy?.HideOverlay();
        }
    }

    // --------------------------------------------------------------- progress

    private void HandleChoiceMarker(ComboBox combo, string line)
    {
        // <marker>|value   — add value to the combo's drop-down (deduplicated, case-insensitive)
        int bar = line.IndexOf('|');
        string value = bar >= 0 ? line[(bar + 1)..].Trim() : "";
        if (value.Length == 0 || combo.IsDisposed) return;
        if (combo.InvokeRequired) { try { combo.BeginInvoke(() => HandleChoiceMarker(combo, line)); } catch { } return; }
        foreach (object? item in combo.Items) if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase)) return;
        combo.Items.Add(value);
    }

    private void UpdateProgress(string activity, int pct, string status)
    {
        if (_closing || IsDisposed) return;
        if (_progress.InvokeRequired) { try { _progress.BeginInvoke(() => UpdateProgress(activity, pct, status)); } catch { } return; }
        _progress.Value = Math.Clamp(pct, 0, 100);
        _progressLabel.Text = string.IsNullOrEmpty(status) ? $"{activity}  ({pct}%)" : $"{activity} — {status}  ({pct}%)";
        _status.Text = activity.Length > 0 ? activity : _status.Text;
    }
}
