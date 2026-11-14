using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AI_FGF;

/// <summary>Where a streamed line came from.</summary>
public enum OutputKind { StdOut, StdErr }

/// <summary>The result of a finished <see cref="ProcessRunner"/> run.</summary>
/// <remarks><see cref="StdOut"/>/<see cref="StdErr"/> are snapshots: they never change after the run returns.</remarks>
public sealed record ProcessResult(int ExitCode, bool Cancelled, IReadOnlyList<string> StdOut, IReadOnlyList<string> StdErr)
{
    private readonly RunOutcome? _outcome;

    /// <summary>True for <see cref="RunOutcome.Success"/> and <see cref="RunOutcome.Warning"/>.</summary>
    public bool Success => Outcome is RunOutcome.Success or RunOutcome.Warning;

    /// <summary>
    /// How the run ended, after the runner's <see cref="ExitCodePolicy"/>. When constructed by
    /// hand without one: timed out / cancelled / exit 0 = success / anything else = failure.
    /// </summary>
    public RunOutcome Outcome
    {
        get => _outcome ?? (TimedOut ? RunOutcome.TimedOut : Cancelled ? RunOutcome.Cancelled : ExitCode == 0 ? RunOutcome.Success : RunOutcome.Failure);
        init => _outcome = value;
    }

    /// <summary>
    /// The first exception thrown by an <see cref="ProcessRunner.Output"/> subscriber, if any. It is
    /// caught (a throwing handler must not kill the process from the reader thread); reading continues.
    /// </summary>
    public Exception? HandlerError { get; init; }

    /// <summary>A readable explanation for a warning or failure (e.g. "Reboot required"); empty on success.</summary>
    public string OutcomeMessage { get; init; } = "";

    /// <summary>True if the run was stopped by <see cref="ProcessRunner.Timeout"/> (not by the caller's token).</summary>
    public bool TimedOut { get; init; }

    /// <summary>When the process was started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>How long the run took, from start until the result was reported.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True if more lines were produced than <see cref="ProcessRunner.CaptureLines"/> kept (the oldest were dropped).</summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Builds and runs an external process, streaming stdout/stderr line-by-line and
/// supporting cancellation (which kills the whole process tree). This is the engine
/// every reference GUI wrote inline around <see cref="Process"/>; here it is reusable
/// and UI-free, so it can be driven from any form or a test.
///
/// Typical use:
/// <code>
/// var run = new ProcessRunner("pwsh")
///     .Arg("-NoProfile").Arg("-File").Arg(scriptPath)
///     .ArgIf(verbose, "-Verbose")
///     .WorkingDirectory(scriptDir)
///     .Env("MYTOOL_PROGRESS", "1");
/// run.Output += (kind, line) => log.Append(line, kind == OutputKind.StdErr ? Color.Salmon : null);
/// ProcessResult result = await run.RunAsync(cts.Token);
/// </code>
///
/// Output is decoded with the console's OEM code page by default — what cmd, native tools,
/// pwsh and .NET programs write when redirected. Use <see cref="Utf8"/> or
/// <see cref="WithEncoding"/> for tools that write UTF-8 (e.g. Python with PYTHONIOENCODING=utf-8).
/// </summary>
public sealed partial class ProcessRunner
{
    private readonly ProcessStartInfo _psi;
    private int _captureLimit = 100_000;
    private TimeSpan? _timeout;
    private readonly ExitCodePolicy _policy = new();

    /// <summary>Fired on the thread pool for every line of output. Marshal to the UI yourself (AiFgfLog does).</summary>
    /// <remarks>Never fired after <see cref="RunAsync"/> has returned.</remarks>
    public event Action<OutputKind, string>? Output;

    static ProcessRunner() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // OEM code pages (866, 437, …)

    public ProcessRunner(string fileName)
    {
        Encoding oem = ConsoleOemEncoding();
        _psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
        };
    }

    // ----- fluent configuration (ArgumentList avoids all quoting pitfalls) -----

    public ProcessRunner Arg(string value) { _psi.ArgumentList.Add(value); return this; }

    public ProcessRunner ArgIf(bool condition, string value)
    {
        if (condition) _psi.ArgumentList.Add(value);
        return this;
    }

    /// <summary>Add <c>name value</c> only when <paramref name="value"/> is non-empty (trimmed).</summary>
    public ProcessRunner Option(string name, string? value)
    {
        value = value?.Trim();
        if (!string.IsNullOrEmpty(value)) { _psi.ArgumentList.Add(name); _psi.ArgumentList.Add(value); }
        return this;
    }

    /// <summary>Add <c>name value</c> with the number formatted invariantly (no "1.234,5" under de-DE).</summary>
    public ProcessRunner Option(string name, decimal value)
        => Option(name, value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Option(string, decimal)"/>
    public ProcessRunner Option(string name, int value)
        => Option(name, value.ToString(CultureInfo.InvariantCulture));

    public ProcessRunner WorkingDirectory(string dir) { _psi.WorkingDirectory = dir; return this; }

    public ProcessRunner Env(string name, string value) { _psi.Environment[name] = value; return this; }

    /// <summary>Decode stdout/stderr with <paramref name="encoding"/> instead of the console OEM code page.</summary>
    public ProcessRunner WithEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _psi.StandardOutputEncoding = encoding;
        _psi.StandardErrorEncoding = encoding;
        return this;
    }

    /// <summary>Decode output as UTF-8 (pwsh with <c>$OutputEncoding</c> set, Python in UTF-8 mode, most cross-platform tools).</summary>
    public ProcessRunner Utf8() => WithEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>
    /// Keep at most the last <paramref name="maxLines"/> lines of each stream in the result
    /// (default 100 000) — a long run can print millions. 0 = keep none, only stream them.
    /// </summary>
    public ProcessRunner CaptureLines(int maxLines)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLines);
        _captureLimit = maxLines;
        return this;
    }

    /// <summary>
    /// Stop the run (killing the whole process tree) if it takes longer than <paramref name="limit"/>.
    /// The result then has <see cref="ProcessResult.TimedOut"/> set — distinct from a cancel
    /// through the caller's token. Hung tools (waiting on stdin, a dead network peer) otherwise
    /// run until someone notices.
    /// </summary>
    public ProcessRunner Timeout(TimeSpan limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero);
        _timeout = limit;
        return this;
    }

    /// <summary>
    /// Say what the tool's exit codes mean, e.g. <c>p => p.Success(0, 7).Warning(3010, "Reboot required")</c>.
    /// Without rules, 0 is success and anything else a failure.
    /// </summary>
    public ProcessRunner ExitCodes(Action<ExitCodePolicy> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_policy);
        return this;
    }

    /// <summary>The configured run time limit, if any.</summary>
    public TimeSpan? TimeoutLimit => _timeout;

    /// <summary>The encoding output is decoded with.</summary>
    public Encoding StreamEncoding => _psi.StandardOutputEncoding!;

    private string? _display;

    /// <summary>The command line as it will be launched, for echoing to a log.</summary>
    public string CommandLine => _display ?? $"{_psi.FileName} {string.Join(' ', _psi.ArgumentList)}";

    /// <summary>Show this instead of the real command line in logs (e.g. instead of an encoded command).</summary>
    public ProcessRunner WithDisplay(string commandLine) { _display = commandLine; return this; }

    /// <summary>The OS process id of the running (or last) process; null before it starts.</summary>
    public int? ProcessId { get; private set; }

    // ------------------------------------------------------------------- run

    public async Task<ProcessResult> RunAsync(CancellationToken token = default)
    {
        CancellationToken callerToken = token;
        var stdout = new Capture(_captureLimit);
        var stderr = new Capture(_captureLimit);
        int finished = 0; // set once we report: no line may be added or streamed after that
        Exception? handlerError = null;
        void Raise(OutputKind kind, string line)
        {
            if (Output is not { } output) return;
            // Each subscriber separately: one that throws must neither starve the others (a
            // multicast Invoke stops at the first exception) nor escape — this runs on a reader
            // thread, where an unhandled exception terminates the process.
            foreach (Action<OutputKind, string> handler in output.GetInvocationList().Cast<Action<OutputKind, string>>())
            {
                try { handler(kind, line); }
                catch (Exception ex)
                {
                    if (Interlocked.CompareExchange(ref handlerError, ex, null) is null) AiFgfDiagnostics.Report(ex, "ProcessRunner.Output handler");
                }
            }
        }

        using var proc = new Process { StartInfo = _psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null || Volatile.Read(ref finished) != 0) return;
            stdout.Add(e.Data);
            Raise(OutputKind.StdOut, e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null || Volatile.Read(ref finished) != 0) return;
            stderr.Add(e.Data);
            Raise(OutputKind.StdErr, e.Data);
        };

        // The run is stopped by either the caller's token or our own timeout; we must be able to
        // tell which one fired afterwards.
        using var timeoutCts = new CancellationTokenSource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
        token = stop.Token;

        var startedAt = DateTimeOffset.Now;
        var clock = Stopwatch.StartNew();
        proc.Start();
        ProcessId = proc.Id;
        if (_timeout is { } limit) timeoutCts.CancelAfter(limit);
        // Everything the tool starts goes into a job, so cancelling reaches processes whose
        // parent has already exited (unreachable by walking parent ids) — see KillTree.
        using JobHandle? job = OperatingSystem.IsWindows() ? JobHandle.TryCreateFor(proc) : null;   // elsewhere: Kill(entireProcessTree)
        // Kill on cancel *synchronously*, inside CancellationTokenSource.Cancel() on the
        // cancelling thread. Waiting for the catch block below would defer the kill to a
        // thread-pool continuation — which never runs if the app exits right after closing
        // its main form, orphaning the whole tree.
        using var killOnCancel = token.Register(() => KillTree(proc, job));
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            // Waits for exit *and* EOF on both pipes (so lines from children that inherited
            // them are still read); the synchronous WaitForExit then drains the last callbacks.
            await proc.WaitForExitAsync(token).ConfigureAwait(false);
            proc.WaitForExit();
            Volatile.Write(ref finished, 1);
            var (outcome, message) = _policy.Classify(proc.ExitCode);
            return new ProcessResult(proc.ExitCode, Cancelled: false, stdout.Snapshot(), stderr.Snapshot())
            {
                Truncated = stdout.Dropped || stderr.Dropped, StartedAt = startedAt, Duration = clock.Elapsed,
                Outcome = outcome, OutcomeMessage = message, HandlerError = handlerError,
            };
        }
        catch (OperationCanceledException)
        {
            KillTree(proc, job); // normally already done by the token registration
            // Give the killed tree a moment to close its pipes so buffered lines land *before*
            // we report — then stop streaming: nothing may arrive after "cancelled".
            try { await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            Volatile.Write(ref finished, 1);
            bool timedOut = timeoutCts.IsCancellationRequested && !callerToken.IsCancellationRequested;
            return new ProcessResult(-1, Cancelled: !timedOut, stdout.Snapshot(), stderr.Snapshot())
            {
                Truncated = stdout.Dropped || stderr.Dropped, TimedOut = timedOut, StartedAt = startedAt, Duration = clock.Elapsed,
                Outcome = timedOut ? RunOutcome.TimedOut : RunOutcome.Cancelled,
                OutcomeMessage = timedOut ? $"timed out after {_timeout}" : "cancelled", HandlerError = handlerError,
            };
        }
    }

    private static void KillTree(Process proc, JobHandle? job)
    {
        job?.Terminate();
        // Also walk the tree by parent id — covers a tool that broke away from the job.
        // No HasExited guard: the root may be gone while its children still run.
        try { proc.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* never started / already reaped */ }
        catch (Win32Exception) { /* access denied / already exiting */ }
        catch (NotSupportedException) { }
    }

    // ------------------------------------------------------------ capture

    /// <summary>Thread-safe "last N lines" buffer.</summary>
    private sealed class Capture(int limit)
    {
        private readonly Queue<string> _lines = new();
        public bool Dropped { get; private set; }

        public void Add(string line)
        {
            if (limit == 0) { Dropped = true; return; }
            lock (_lines)
            {
                if (_lines.Count == limit) { _lines.Dequeue(); Dropped = true; }
                _lines.Enqueue(line);
            }
        }

        public IReadOnlyList<string> Snapshot() { lock (_lines) return _lines.ToArray(); }
    }

    // ------------------------------------------------------- OEM code page

    private static Encoding ConsoleOemEncoding()
    {
        if (!OperatingSystem.IsWindows()) return new UTF8Encoding(false);   // Linux/macOS consoles are UTF-8
        try { return Encoding.GetEncoding((int)GetOEMCP()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or EntryPointNotFoundException or DllNotFoundException)
        { return new UTF8Encoding(false); }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    // --------------------------------------------------------- job object

    /// <summary>A Win32 job holding the tool and every process it starts (unless they break away).</summary>
    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private JobHandle() : base(ownsHandle: true) { }

        public static JobHandle? TryCreateFor(Process proc)
        {
            try
            {
                JobHandle job = CreateJobObject(IntPtr.Zero, null);
                if (job.IsInvalid) { job.Dispose(); return null; }
                if (!AssignProcessToJobObject(job, proc.Handle)) { job.Dispose(); return null; }
                return job;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
            {
                return null;
            }
        }

        public void Terminate() { if (!IsInvalid && !IsClosed) TerminateJobObject(this, 1); }

        protected override bool ReleaseHandle() => CloseHandle(handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern JobHandle CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(JobHandle job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(JobHandle job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
