using System;

namespace AI_FGF;

/// <summary>
/// Where the core reports errors it catches and survives (a throwing output handler, …). A front
/// end's crash handling plugs in here (WinForms: <c>AiFgfCrash.Install</c>); unset, they are only
/// returned to the caller (e.g. <see cref="ProcessResult.HandlerError"/>).
/// </summary>
public static class AiFgfDiagnostics
{
    /// <summary>Receives (exception, context) and returns where it was recorded, or null.</summary>
    public static Func<Exception, string, string?>? ErrorReporter { get; set; }

    public static string? Report(Exception ex, string context) => ErrorReporter?.Invoke(ex, context);
}
