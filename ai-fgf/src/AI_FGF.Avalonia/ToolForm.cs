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
public abstract class ToolForm : Window, IToolRunView
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
    private readonly Border _busy;
    // the controller: validate -> build -> run -> report, shared with the WinForms ToolForm (this window is its view)
    private readonly ToolRunController _controller;
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
    protected virtual string FormTitle => "AI Forms Generation Framework tool";
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
        _controller = new ToolRunController(this);
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
        _controller.Cancel();
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
                Fields = _fields.GetPersistableValues(),   // never a password
                Theme = ThemeButton ? AiFgfTheme.Current.Choice.ToString() : null,
                Buttons = ThemeButton && AiFgfEffects.UserButtons is { } b ? AiFgfEffects.Name(b) : null,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    /// <summary>Save the field values as a JSON preset (reported in the log; false when it could not be written).</summary>
    protected bool SavePresetTo(string path) => ToolPresets.Save(path, _fields, ((IToolRunView)this).AppendLog);

    /// <summary>Apply a JSON preset to the fields; false (with a log line) if it had no values.</summary>
    protected bool LoadPresetFrom(string path) => ToolPresets.Load(path, _fields, ((IToolRunView)this).AppendLog);

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

    private void CancelRun() => _controller.Cancel();

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
    void IToolRunView.SetRunning(bool running) => SetRunning(running);
    void IToolRunView.ShowProgress(string activity, int percent, string status) => UpdateProgress(activity, percent, status);
    void IToolRunView.RunFinished(ProcessResult result) => OnRunFinished(result);
    bool IToolRunView.HandleOutputLine(string text) => false;

    void IToolRunView.SetStatus(string text)
    {
        if (_closing) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => _status.Text = text); return; }
        _status.Text = text;
    }

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

    private void SetRunning(bool running)
    {
        _running = running;
        if (_closing) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => SetRunning(running)); return; }
        _runButton.IsEnabled = !running;
        _cancelButton.IsEnabled = running;
#if NET11_0_OR_GREATER
        foreach (Control action in _actionButtons) action.IsEnabled = !running;
#endif
        _status.Text = running ? AiFgfText.Running : AiFgfText.Idle;
        _busy.IsVisible = running && ShowBusyOverlay;
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
    public static string FormatDuration(TimeSpan d) => ToolRunController.FormatDuration(d);
}
