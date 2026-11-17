using System.Globalization;

namespace AI_FGF;

/// <summary>How a line the controller writes to the log reads: the view maps it to its theme's colours.</summary>
public enum RunLogTone { Plain, Accent, Success, Warning, Error }

/// <summary>
/// The view of a tool run — what <see cref="ToolRunController"/> needs from a window. The WinForms and the Avalonia
/// <c>ToolForm</c> implement it; a test implements it without any window. Calls may come from a background thread
/// (process output): the view marshals to its UI thread where it must.
/// </summary>
public interface IToolRunView
{
    /// <summary>Check the inputs; the view marks the invalid ones itself. Empty = valid.</summary>
    IReadOnlyList<FieldError> ValidateInputs();

    /// <summary>The process to run from the current inputs, or null to abort (the view reports why).</summary>
    ProcessRunner? BuildRun();

    /// <summary>The limit for a run that sets none of its own; null = none.</summary>
    TimeSpan? RunTimeout { get; }

    /// <summary>The line that starts a progress report: <c>marker|activity|percent|status</c>.</summary>
    string ProgressMarker { get; }

    /// <summary>True once the window is closing: no more results are handed to it.</summary>
    bool IsClosing { get; }

    void AppendLog(string text, RunLogTone tone);

    void SetStatus(string text);

    /// <summary>Enable Cancel instead of Run, show the busy state (or undo that).</summary>
    void SetRunning(bool running);

    void ShowProgress(string activity, int percent, string status);

    /// <summary>A line of output the view handles itself (e.g. a marker that fills a drop-down); true = consumed.</summary>
    bool HandleOutputLine(string text);

    /// <summary>The run is over (not called when it never started, nor while closing).</summary>
    void RunFinished(ProcessResult result);
}

/// <summary>
/// The controller of a tool form: validate the inputs → build the command → run it → report. Output is turned back
/// from PowerShell CLIXML to text, progress lines move the progress bar, the outcome (success, warnings, failure,
/// cancelled, timed out) is logged with the exit-code policy's wording, and every exception — from validation, the
/// view's <see cref="IToolRunView.BuildRun"/>, starting the process or <see cref="IToolRunView.RunFinished"/> — is
/// reported in the log with the running state always cleared. No UI types: one controller for every front end.
/// </summary>
public sealed class ToolRunController
{
    private readonly IToolRunView _view;
    private readonly CliXmlLogFilter _clixml = new();
    private CancellationTokenSource? _cts;
    private volatile bool _running;

    public ToolRunController(IToolRunView view) => _view = view ?? throw new ArgumentNullException(nameof(view));

    /// <summary>True from the moment the process is about to start until its run is reported.</summary>
    public bool IsRunning => _running;

    /// <summary>Stop the running process tree (no-op when nothing runs).</summary>
    public void Cancel()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>One run. Never throws: the click handler that starts it discards the task.</summary>
    public async Task RunAsync()
    {
        if (_running) return;
        bool started = false;
        ProcessRunner? runner = null;
        try
        {
            // Non-modal validation: invalid fields are marked, errors go to the log and the status bar.
            var errors = _view.ValidateInputs();
            if (errors.Count > 0)
            {
                foreach (FieldError err in errors) _view.AppendLog("✗ " + err.Message, RunLogTone.Error);
                _view.SetStatus(errors.Count == 1 ? errors[0].Message : AiFgfText.Format(AiFgfText.FieldsNeedAttention, errors.Count));
                return;
            }

            runner = _view.BuildRun();
            if (runner is null) return;   // the view aborted (and reported why)

            _cts = new CancellationTokenSource();
            _clixml.Reset();
            runner.Output += OnOutput;
            if (runner.TimeoutLimit is null && _view.RunTimeout is { } limit) runner.Timeout(limit);

            started = true;
            _running = true;
            _view.SetRunning(true);
            _view.ShowProgress(AiFgfText.Starting, 0, "");
            _view.AppendLog($"> {runner.CommandLine}", RunLogTone.Accent);

            ProcessResult result = await runner.RunAsync(_cts.Token);
            foreach (LogLine rendered in _clixml.Flush()) Emit(rendered);   // any CLIXML left mid-batch
            if (result.HandlerError is { } handlerError)
                _view.AppendLog($"ERROR in an output handler: {handlerError.GetType().Name}: {handlerError.Message}", RunLogTone.Error);

            string took = FormatDuration(result.Duration);
            if (result.TimedOut)
            {
                _view.AppendLog($"[timed out after {took} — process tree stopped]", RunLogTone.Warning);
                _view.ShowProgress(AiFgfText.TimedOut, 0, "");
            }
            else if (result.Cancelled)
            {
                _view.AppendLog($"[cancelled after {took}]", RunLogTone.Warning);
                _view.ShowProgress(AiFgfText.Cancelled, 0, "");
            }
            else
            {
                // tone and wording follow the exit-code policy (warnings are not errors)
                var (tone, activity) = result.Outcome switch
                {
                    RunOutcome.Success => (RunLogTone.Success, AiFgfText.Done),
                    RunOutcome.Warning => (RunLogTone.Warning, AiFgfText.DoneWithWarnings),
                    _ => (RunLogTone.Error, AiFgfText.DoneWithErrors),
                };
                string detail = result.OutcomeMessage.Length > 0 && !result.OutcomeMessage.StartsWith("exit code", StringComparison.Ordinal)
                    ? $" — {result.OutcomeMessage}" : "";
                _view.AppendLog($"[finished — exit code {result.ExitCode} in {took}{detail}]", tone);
                _view.ShowProgress(activity, 100, result.OutcomeMessage);
            }

            try { if (!_view.IsClosing) _view.RunFinished(result); }
            catch (Exception ex) { _view.AppendLog("post-run: " + ex.Message, RunLogTone.Error); }
        }
        catch (Exception ex)
        {
            _view.AppendLog("ERROR: " + ex.Message, RunLogTone.Error);
            _view.ShowProgress(AiFgfText.Error, 0, "");
            if (!started) _view.SetStatus(AiFgfText.Error + ": " + ex.Message);
        }
        finally
        {
            if (runner is not null) runner.Output -= OnOutput;
            if (started) { _running = false; _view.SetRunning(false); }
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnOutput(OutputKind kind, string line)
    {
        // Convert any PowerShell CLIXML (minishell / -OutputFormat Xml) back to text; other output passes straight
        // through. A batch may span several lines, so this can emit 0 or many.
        foreach (LogLine rendered in _clixml.Feed(kind, line)) Emit(rendered);
    }

    private void Emit(LogLine rendered)
    {
        if (_view.HandleOutputLine(rendered.Text)) return;
        if (rendered.Text.StartsWith(_view.ProgressMarker, StringComparison.Ordinal))
        {
            var (activity, percent, status) = ParseProgress(rendered.Text);
            _view.ShowProgress(activity, percent, status);
            return;
        }
        _view.AppendLog(rendered.Text, rendered.Severity switch
        {
            LogSeverity.Error => RunLogTone.Error,
            LogSeverity.Warning => RunLogTone.Warning,
            _ => RunLogTone.Plain,
        });
    }

    /// <summary><c>marker|activity|percent|status</c> (the status may itself contain '|').</summary>
    public static (string Activity, int Percent, string Status) ParseProgress(string line)
    {
        string[] parts = line.Split('|', 4);
        string activity = parts.Length > 1 ? parts[1] : "";
        int pct = 0;
        if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
            pct = (int)Math.Round(d);
        string status = parts.Length > 3 ? parts[3] : "";
        return (activity, pct, status);
    }

    /// <summary>"0.4 s", "12.3 s", "4:05", "1:02:03".</summary>
    public static string FormatDuration(TimeSpan d)
    {
        if (d < TimeSpan.FromMinutes(1)) return d.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        if (d < TimeSpan.FromHours(1)) return d.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        return ((int)d.TotalHours).ToString(CultureInfo.InvariantCulture) + d.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
