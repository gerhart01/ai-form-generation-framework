using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>How <see cref="AiFgfCrash"/> handles an unexpected exception.</summary>
public sealed class AiFgfCrashOptions
{
    /// <summary>Where reports go; default <c>%LocalAppData%\AI_FGF\crash\{exe}</c>.</summary>
    public string? Directory { get; set; }

    /// <summary>
    /// Called for an exception on the UI thread after the report is written (path may be null if
    /// writing failed). Default: a dialog naming the report; the app keeps running.
    /// </summary>
    public Action<Exception, string?>? OnCrash { get; set; }

    /// <summary>How many of the most recent log lines to include in a report.</summary>
    public int LogLines { get; set; } = 200;
}

/// <summary>
/// Crash handling for tool apps: an unexpected exception is written to a report file (exe,
/// version, OS, runtime, DPI, thread, context, the exception, and the tail of the active tool's
/// log) instead of a bare WinForms dialog — or, on a background thread, instead of the process
/// vanishing without a trace. <see cref="AiFgfApp.Run"/> installs it by default.
/// </summary>
public static class AiFgfCrash
{
    private static AiFgfCrashOptions? _options;
    private static Func<string[]>? _logTail;
    private static int _sequence;

    /// <summary>True while installed.</summary>
    public static bool IsInstalled => _options is not null;

    public static string DefaultDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI_FGF", "crash",
                        Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "app"));

    /// <summary>Hook the UI-thread, background-thread and unobserved-task exception events.</summary>
    public static void Install(AiFgfCrashOptions? options = null)
    {
        _options = options ?? new AiFgfCrashOptions();
        AiFgfDiagnostics.ErrorReporter = Report;   // errors the core survives (output handlers) get a report too
        try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); }
        catch (InvalidOperationException) { /* windows already exist: ThreadException still fires for UI exceptions */ }
        Application.ThreadException -= OnThreadException;
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
    }

    /// <summary>Remove the hooks (tests, or an app that takes over).</summary>
    public static void Uninstall()
    {
        Application.ThreadException -= OnThreadException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
        if (AiFgfDiagnostics.ErrorReporter == Report) AiFgfDiagnostics.ErrorReporter = null;
        _options = null;
    }

    /// <summary>Where the most recent log lines come from (ToolForm registers its log).</summary>
    public static void SetLogSource(Func<string[]>? tail) => _logTail = tail;

    /// <summary>
    /// Record a non-fatal error (e.g. an exception in an output handler) when crash handling is
    /// installed. Returns the report path, or null.
    /// </summary>
    public static string? Report(Exception ex, string context)
        => _options is null ? null : TryWrite(ex, _options.Directory, context);

    /// <summary>Write a report and return its path. Never overwrites an earlier report.</summary>
    public static string WriteReport(Exception ex, string? directory = null, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        string dir = directory ?? _options?.Directory ?? DefaultDirectory;
        System.IO.Directory.CreateDirectory(dir);
        string name = $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Interlocked.Increment(ref _sequence)}.txt";
        string path = Path.Combine(dir, name);

        var sb = new StringBuilder();
        Assembly? entry = Assembly.GetEntryAssembly();
        sb.AppendLine($"Time      {DateTimeOffset.Now:O}");
        sb.AppendLine($"Exe       {Environment.ProcessPath}");
        sb.AppendLine($"Version   {entry?.GetName().Version}");
        sb.AppendLine($"OS        {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Runtime   {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Culture   {CultureInfo.CurrentCulture.Name} / UI {CultureInfo.CurrentUICulture.Name}");
        sb.AppendLine($"DPI       {DpiInfo()}");
        sb.AppendLine($"Thread    {Thread.CurrentThread.ManagedThreadId} {Thread.CurrentThread.Name} (UI={Thread.CurrentThread.GetApartmentState() == ApartmentState.STA})");
        if (context is not null) sb.AppendLine($"Context   {context}");
        sb.AppendLine();
        sb.AppendLine(ex.ToString());

        string[] tail = Array.Empty<string>();
        try { tail = _logTail?.Invoke() ?? Array.Empty<string>(); } catch (Exception) { /* the log may be gone */ }
        int keep = _options?.LogLines ?? 200;
        if (tail.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"--- last {Math.Min(keep, tail.Length)} log line(s) ---");
            foreach (string line in tail.Skip(Math.Max(0, tail.Length - keep))) sb.AppendLine(line);
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private static string? TryWrite(Exception ex, string? dir, string context)
    {
        try { return WriteReport(ex, dir, context); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string DpiInfo()
    {
        Form? main = Application.OpenForms.Cast<Form>().FirstOrDefault();
        return main is null ? "(no window)" : $"{main.DeviceDpi} ({main.DeviceDpi * 100 / 96}%)";
    }

    // ------------------------------------------------------------- handlers

    private static void OnThreadException(object? sender, ThreadExceptionEventArgs e)
    {
        string? path = TryWrite(e.Exception, _options?.Directory, "UI thread");
        if (_options?.OnCrash is { } onCrash) { onCrash(e.Exception, path); return; }
        MessageBox.Show(
            AiFgfText.Format(AiFgfText.CrashMessage, e.Exception.Message,
                          path is null ? AiFgfText.CrashReportFailed : AiFgfText.Format(AiFgfText.CrashReportSaved, path)),
            AiFgfText.CrashTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static void OnUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        // A background thread failed: the runtime will terminate the process — leave a trace.
        if (e.ExceptionObject is Exception ex) TryWrite(ex, _options?.Directory, $"background thread (terminating={e.IsTerminating})");
    }

    private static void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryWrite(e.Exception, _options?.Directory, "unobserved task");
        e.SetObserved();
    }
}
