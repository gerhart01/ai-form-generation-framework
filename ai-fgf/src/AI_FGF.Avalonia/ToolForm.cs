using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AI_FGF.Avalonia;

/// <summary>
/// A complete tool window — the Avalonia twin of the WinForms <c>ToolForm</c>, with the same
/// members, so a tool written against one compiles against the other:
/// <code>
/// public sealed class PingForm : ToolForm
/// {
///     protected override void BuildParameters(FormScaffold.RowBuilder rows)
///         => rows.Text("Host:", value: "127.0.0.1", required: true).Number("Count:", min: 1, max: 100, value: 4);
///     protected override ProcessRunner? BuildRun()
///         => new ProcessRunner("ping").Option("-n", Fields.Int("Count")).Arg(Fields.Text("Host"));
/// }
/// </code>
/// Parameters on top, Run/Cancel (+ presets, theme), progress, Output/Results tabs, status bar;
/// validation before a run, a coloured log, <c>##PROGRESS##|activity|percent|status</c> lines
/// drive the progress bar, exit-code outcomes, settings (fields, placement, theme) saved per tool.
/// Keyboard: Ctrl+Enter / F5 run, Esc cancels a run, Ctrl+F finds in the log.
/// </summary>
public abstract class ToolForm : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    private readonly AiFgfLog _log = new();
    private readonly AiFgfDataGridPanel _results = new();
    private readonly FieldSet _fields;
    private readonly TabControl _tabs = new();
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 18, MinHeight = 18, CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(1) };
    private readonly TextBlock _progressLabel = new() { Text = AiFgfText.Idle, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 1.9) };
    private readonly TextBlock _status = new()
    {
        Text = AiFgfText.Idle, Classes = { AiFgfSnapshot.StatusClass },
        FontSize = WinFormsMetrics.StatusFontSize, Margin = WinFormsMetrics.StatusMargin, HorizontalAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(WinFormsMetrics.TextPad(WinFormsMetrics.StatusFontSize), 0),
    };
    private Button _runButton = null!, _cancelButton = null!;
    private readonly CliXmlLogFilter _clixml = new();
    private readonly Border _busy;
    private CancellationTokenSource? _cts;
    private bool _running, _closing;

    protected AiFgfLog Log => _log;
    protected FieldSet Fields => _fields;
    protected AiFgfDataGridPanel Results => _results;

    /// <summary>Settings key; null = don't persist (tests). Default: the type name.</summary>
    protected virtual string? SettingsKey => GetType().FullName;
    /// <summary>
    /// Off: no tool window reads or writes settings files (tests, kiosk/portable use). Default on.
    /// </summary>
    public static bool PersistSettings { get; set; } = true;

    protected virtual string? SettingsPath => PersistSettings && SettingsKey is { } key ? AiFgfSettings.DefaultPath(key) : null;
    protected virtual TimeSpan? RunTimeout => null;
    protected virtual bool PresetButtons => false;
    protected virtual bool ShowBusyOverlay => false;
    protected string ProgressMarker { get; set; } = "##PROGRESS##";
    protected virtual string RunButtonText => AiFgfText.Run;
    protected virtual KeyGesture RunShortcut => new(Key.Enter, KeyModifiers.Control);
    protected virtual KeyGesture CancelShortcut => new(Key.Escape);
    protected virtual string FormTitle => "AI_FGF Tool";
    protected virtual bool ThemeButton => false;
    /// <summary>
    /// This form's button style (flat, raised, glowing — <see cref="AiFgfButtonEffects"/>; WinForms draws the effects with
    /// Direct3D 12). Default: <see cref="AiFgfEffects.DefaultButtons"/>; the user's pick under "Theme…" and <c>AI_FGF_BUTTONS</c> win.
    /// </summary>
    protected virtual AiFgfButtonStyle ButtonStyle => AiFgfEffects.DefaultButtons;

    /// <summary>
    /// Render buttons and field focus glow with the real Direct3D 12 renderer (AI_FGF's
    /// WinForms look) instead of the cross-platform drop shadow — only on Windows, and only when the
    /// app referenced the experimental <c>AI_FGF.Avalonia.Direct3D</c> add-on and it could
    /// initialise (<see cref="AiFgfRenderers.Direct3D"/>); otherwise this is ignored and the drop shadow
    /// is used, unchanged. <see cref="SpecToolForm"/> sets this from a <c>tool.json</c>'s
    /// <c>"renderer": "direct3d"</c>.
    /// </summary>
    protected virtual bool UseDirect3D => false;

    /// <summary>True once <see cref="UseDirect3D"/> was honoured: the add-on was referenced and available.</summary>
    public bool Direct3DActive { get; private set; }

    /// <summary>The style the buttons show now.</summary>
    public AiFgfButtonStyle EffectiveButtonStyle => AiFgfEffects.Resolve(ButtonStyle);

    /// <summary>The title (the WinForms <c>Form.Text</c>), so code setting <c>Text = "…"</c> works on both.</summary>
    public string Text { get => Title ?? ""; set => Title = value; }

    /// <summary>The Run button (tests, automation).</summary>
    public Button RunButton => _runButton;
    public Button CancelRunButton => _cancelButton;
    public bool IsRunning => _running;
    /// <summary>Completes when the current run has finished.</summary>
    public Task? CurrentRun { get; private set; }
    /// <summary>The fields (public view for tests and automation).</summary>
    public FieldSet FieldValues => _fields;
    public AiFgfLog LogView => _log;
    public AiFgfDataGridPanel ResultsView => _results;

    protected ToolForm()
    {
        Direct3DActive = UseDirect3D && AiFgfRenderers.Direct3D is { IsAvailable: true };
        var scaffold = FormScaffold.For(this, Direct3DActive)
            .Title(FormTitle)
            .Size(1040, 760)
            .Parameters(AiFgfText.Parameters, BuildParameters)
            .Buttons(bar =>
            {
                bar.Primary(RunButtonText, (_, _) => StartRun(), out _runButton);
                bar.Button(AiFgfText.CancelRun, (_, _) => CancelRun(), out _cancelButton);
                ConfigureButtons(bar);
                if (PresetButtons)
                {
                    bar.Button(AiFgfText.SavePreset, async (_, _) => { if (await AiFgf.PickSaveFileAsync(this, AiFgfText.PresetFilter, AiFgfText.SavePreset, "preset.json") is { } p) SavePresetTo(p); }, out _);
                    bar.Button(AiFgfText.LoadPreset, async (_, _) => { if (await AiFgf.PickOpenFileAsync(this, AiFgfText.PresetFilter, AiFgfText.LoadPreset) is { } p) LoadPresetFrom(p); }, out _);
                }
                if (ThemeButton) bar.Button(AiFgfText.ThemeButton, async (_, _) => await AiFgfThemeDialog.ShowAsync(this), out _);
            })
            .Body(BuildBody());
        scaffold.MinBodyHeight = 220;
        _fields = scaffold.Fields;
        _cancelButton.IsEnabled = false;
        scaffold.Done();

        // status bar at the bottom, the busy overlay above everything
        var status = AvTheme.Bind(new Border { Child = _status }, Border.BackgroundProperty, AvTheme.HeaderKey);
        AvTheme.Bind(_status, TextBlock.ForegroundProperty, AvTheme.SubtleKey);
        var dock = new DockPanel();
        DockPanel.SetDock(status, Dock.Bottom);
        dock.Children.Add(status);
        Grid root = scaffold.Root!;
        Content = null;               // Done() made it the window's content: detach before re-parenting
        dock.Children.Add(root);
        _busy = new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)),
            Child = new TextBlock { Text = AiFgfText.Working, FontSize = AvTheme.UiFontSize * 1.6, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Content = new Panel { Children = { dock, _busy } };

        AddHandler(KeyDownEvent, OnKeys, RoutingStrategies.Tunnel);
    }

    private Control BuildBody()
    {
        AvTheme.Bind(_progress, ProgressBar.BackgroundProperty, AvTheme.TrackKey);
        AvTheme.Bind(_progress, ProgressBar.ForegroundProperty, AvTheme.AccentKey);
        AvTheme.Bind(_progress, ProgressBar.BorderBrushProperty, AvTheme.BorderKey);
        // WinForms: a 44-px panel, the label filling it above an 18-px bar
        _progress.Height = _progress.MinHeight = WinFormsMetrics.ProgressBarHeight;
        DockPanel.SetDock(_progress, Dock.Bottom);
        var progressPanel = new DockPanel
        {
            Height = WinFormsMetrics.ProgressPanelHeight - WinFormsMetrics.ProgressPadding.Top - WinFormsMetrics.ProgressPadding.Bottom,
            Margin = WinFormsMetrics.ProgressPadding,
            Children = { _progress, _progressLabel },
        };
        static Border Page(Control content) => AvTheme.Bind(new Border
        {
            BorderThickness = new Thickness(1), Padding = WinFormsMetrics.TabPagePadding, Child = content,
        }, Border.BorderBrushProperty, AvTheme.BorderKey);
        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = AiFgfText.Output, Content = Page(_log) },
            new TabItem { Header = AiFgfText.Results, Content = Page(_results) },
        };
        var body = new DockPanel();
        DockPanel.SetDock(progressPanel, Dock.Top);
        body.Children.Add(progressPanel);
        body.Children.Add(_tabs);
        return body;
    }

    /// <summary>Add the parameter rows.</summary>
    protected abstract void BuildParameters(FormScaffold.RowBuilder rows);

    /// <summary>The command to run (fields are valid); null aborts.</summary>
    protected abstract ProcessRunner? BuildRun();

    /// <summary>Add tool-specific buttons after Run/Cancel.</summary>
    protected virtual void ConfigureButtons(FormScaffold.BarBuilder bar) { }

    /// <summary>After a run (parse output, show results…).</summary>
    protected virtual void OnRunFinished(ProcessResult result) { }

    protected void ShowResultsTab() => _tabs.SelectedIndex = 1;

    // ---------------------------------------------------------- lifecycle

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        bool placed = false;
        if (SettingsPath is { } path)
        {
            var data = AiFgfSettings.Load(path);
            _fields.SetValues(data.Fields);
            if (ThemeButton && AiFgfThemeChoice.TryParse(data.Theme, out AiFgfThemeChoice saved) && !saved.Equals(AiFgfTheme.Current.Choice))
                AiFgfTheme.Switch(saved.ToTheme());
            if (ThemeButton && AiFgfEffects.TryParse(data.Buttons, out AiFgfButtonStyle buttons)) AiFgfEffects.UserButtons = buttons;
            if (data.Window is { } placement) { AiFgfSettings.Restore(this, placement); placed = true; }
        }
        AiFgfButtonEffects.Attach(this, () => EffectiveButtonStyle);
        if (Direct3DActive)
        {
            void RefreshD3D(object? sender, EventArgs e2) => AiFgfRenderers.Direct3D!.Refresh(this, EffectiveButtonStyle);
            RefreshD3D(null, EventArgs.Empty);
            AiFgfTheme.Changed += RefreshD3D;
            AiFgfEffects.Changed += RefreshD3D;
            Closed += (_, _) => { AiFgfTheme.Changed -= RefreshD3D; AiFgfEffects.Changed -= RefreshD3D; };
        }
        if (!placed) FitDefaultSizeToScreen();
        string? first = _fields.Keys.FirstOrDefault(k => _fields.IsActive(k) && _fields[k].IsEnabled);
        if (first is not null) _fields[first].Focus();
    }

    /// <summary>The default size is logical: on a small or high-scale screen shrink it to the working area.</summary>
    private void FitDefaultSizeToScreen()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        double w = screen.WorkingArea.Width / screen.Scaling, h = screen.WorkingArea.Height / screen.Scaling;
        if (Width > w) Width = Math.Max(MinWidth, w);
        if (Height > h) Height = Math.Max(MinHeight, h);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        SaveSettings();
        base.OnClosed(e);
    }

    private void SaveSettings()
    {
        if (SettingsPath is not { } path) return;
        try
        {
            AiFgfSettings.Save(path, new FormSettingsData
            {
                Window = AiFgfSettings.Capture(this),
                Fields = _fields.GetValues(),
                Theme = ThemeButton ? AiFgfTheme.Current.Choice.ToString() : null,
                Buttons = ThemeButton && AiFgfEffects.UserButtons is { } b ? AiFgfEffects.Name(b) : null,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    protected bool SavePresetTo(string path)
    {
        try { AiFgfSettings.SavePreset(path, _fields); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _log.Error($"Could not save the preset: {ex.Message}");
            return false;
        }
    }

    protected bool LoadPresetFrom(string path)
    {
        bool ok = AiFgfSettings.LoadPreset(path, _fields);
        if (!ok) _log.Warn($"No values in {path}");
        return ok;
    }

    private void OnKeys(object? sender, KeyEventArgs e)
    {
        if ((RunShortcut.Matches(e) || (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)) && !_running && _runButton.IsEnabled)
        { StartRun(); e.Handled = true; }
        else if (CancelShortcut.Matches(e) && _running) { CancelRun(); e.Handled = true; }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) { _tabs.SelectedIndex = 0; _log.FocusFind(); e.Handled = true; }
    }

    // --------------------------------------------------------------- run loop

    /// <summary>What Run, Ctrl+Enter and F5 do.</summary>
    public void StartRun()
    {
        if (_running) return;
        CurrentRun = RunAsync();
    }

    private void CancelRun() { try { _cts?.Cancel(); } catch (ObjectDisposedException) { } }

    private async Task RunAsync()
    {
        if (_running) return;
        bool started = false;
        ProcessRunner? runner = null;
        try
        {
            var errors = _fields.Validate();
            if (errors.Count > 0)
            {
                foreach (FieldError err in errors) _log.Error("✗ " + err.Message);
                _status.Text = errors.Count == 1 ? errors[0].Message : AiFgfText.Format(AiFgfText.FieldsNeedAttention, errors.Count);
                return;
            }

            runner = BuildRun();
            if (runner is null) return;

            _cts = new CancellationTokenSource();
            _clixml.Reset();
            runner.Output += OnOutput;
            if (runner.TimeoutLimit is null && RunTimeout is { } limit) runner.Timeout(limit);

            started = true;
            SetRunning(true);
            UpdateProgress(AiFgfText.Starting, 0, "");
            _log.Append($"> {runner.CommandLine}", AiFgfTheme.Current.LogAccent);

            ProcessResult result = await runner.RunAsync(_cts.Token);
            foreach (LogLine rendered in _clixml.Flush()) Emit(rendered);   // any CLIXML left mid-batch
            if (result.HandlerError is { } handlerError)
                _log.Error($"ERROR in an output handler: {handlerError.GetType().Name}: {handlerError.Message}");

            string took = FormatDuration(result.Duration);
            if (result.TimedOut)
            {
                _log.Warn($"[timed out after {took} — process tree stopped]");
                UpdateProgress(AiFgfText.TimedOut, 0, "");
            }
            else if (result.Cancelled)
            {
                _log.Warn($"[cancelled after {took}]");
                UpdateProgress(AiFgfText.Cancelled, 0, "");
            }
            else
            {
                var (colour, activity) = result.Outcome switch
                {
                    RunOutcome.Success => (AiFgfTheme.Current.LogSuccess, AiFgfText.Done),
                    RunOutcome.Warning => (AiFgfTheme.Current.LogWarn, AiFgfText.DoneWithWarnings),
                    _ => (AiFgfTheme.Current.LogError, AiFgfText.DoneWithErrors),
                };
                string detail = result.OutcomeMessage.Length > 0 && !result.OutcomeMessage.StartsWith("exit code", StringComparison.Ordinal)
                    ? $" — {result.OutcomeMessage}" : "";
                _log.Append($"[finished — exit code {result.ExitCode} in {took}{detail}]", colour);
                UpdateProgress(activity, 100, result.OutcomeMessage);
            }

            try { if (!_closing) OnRunFinished(result); } catch (Exception ex) { _log.Error("post-run: " + ex.Message); }
        }
        catch (Exception ex)
        {
            _log.Error("ERROR: " + ex.Message);
            UpdateProgress(AiFgfText.Error, 0, "");
            if (!started) _status.Text = AiFgfText.Error + ": " + ex.Message;
        }
        finally
        {
            if (runner is not null) runner.Output -= OnOutput;
            if (started) SetRunning(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnOutput(OutputKind kind, string line)
    {
        // Convert any PowerShell CLIXML (minishell / -OutputFormat Xml) back to text; other output
        // passes straight through. A batch may span several lines, so this can emit 0 or many.
        foreach (LogLine rendered in _clixml.Feed(kind, line)) Emit(rendered);
    }

    private void Emit(LogLine rendered)
    {
        if (rendered.Text.StartsWith(ProgressMarker, StringComparison.Ordinal)) { HandleProgress(rendered.Text); return; }
        switch (rendered.Severity)
        {
            case LogSeverity.Error: _log.Error(rendered.Text); break;
            case LogSeverity.Warning: _log.Warn(rendered.Text); break;
            default: _log.Line(rendered.Text); break;
        }
    }

    private void SetRunning(bool running)
    {
        _running = running;
        if (_closing) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => SetRunning(running)); return; }
        _runButton.IsEnabled = !running;
        _cancelButton.IsEnabled = running;
        _status.Text = running ? AiFgfText.Running : AiFgfText.Idle;
        _busy.IsVisible = running && ShowBusyOverlay;
    }

    private void HandleProgress(string line)
    {
        // ##PROGRESS##|<activity>|<percent>|<status>
        string[] parts = line.Split('|', 4);
        string activity = parts.Length > 1 ? parts[1] : "";
        int pct = 0;
        if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) pct = (int)Math.Round(d);
        string status = parts.Length > 3 ? parts[3] : "";
        UpdateProgress(activity, pct, status);
    }

    private void UpdateProgress(string activity, int pct, string status)
    {
        if (_closing) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => UpdateProgress(activity, pct, status)); return; }
        _progress.Value = Math.Clamp(pct, 0, 100);
        _progressLabel.Text = string.IsNullOrEmpty(status) ? $"{activity}  ({pct}%)" : $"{activity} — {status}  ({pct}%)";
        if (activity.Length > 0) _status.Text = activity;
    }

    /// <summary>"0.4 s", "12.3 s", "4:05", "1:02:03".</summary>
    public static string FormatDuration(TimeSpan d)
    {
        if (d < TimeSpan.FromMinutes(1)) return d.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        if (d < TimeSpan.FromHours(1)) return d.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        return ((int)d.TotalHours).ToString(CultureInfo.InvariantCulture) + d.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
