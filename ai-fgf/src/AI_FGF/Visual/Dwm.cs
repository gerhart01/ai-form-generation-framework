using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>The Windows 11 window backdrop material (maps to DWM's SYSTEMBACKDROP_TYPE).</summary>
public enum BackdropKind
{
    Auto = 0,
    None = 1,
    /// <summary>Mica — a subtle, desktop-tinted material for long-lived windows.</summary>
    Mica = 2,
    /// <summary>Acrylic — a stronger, translucent blur for transient surfaces.</summary>
    Acrylic = 3,
    /// <summary>Tabbed — a Mica variant tuned for tabbed windows.</summary>
    Tabbed = 4,
}

/// <summary>
/// Opt-in modern window dressing via DWM: a Windows 11 backdrop material (Mica/Acrylic), a
/// dark title bar that matches the theme, and rounded corners. Every call is a safe no-op on
/// Windows 10 and earlier (and off-Windows), so it is always safe to call.
///
/// <code>Dwm.Apply(form, BackdropKind.Mica);</code>
/// </summary>
public static class Dwm
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int CORNER_ROUND = 2;
    private const int CORNER_DONOTROUND = 1;   // 0 would be "system default" — rounded on Windows 11

    private sealed record Settings(BackdropKind Backdrop, bool? Dark, bool Rounded);
    private static readonly ConditionalWeakTable<Form, Settings> Wanted = new();
    private static readonly ConditionalWeakTable<Form, DwmResult> Results = new();

    /// <summary>
    /// Apply a backdrop, a dark/light title bar and rounded (or square) corners to the form.
    /// If <paramref name="dark"/> is null it follows the current theme's background luminance.
    /// The settings stay with the form: DWM attributes belong to a window handle, so they are
    /// re-applied every time the handle is (re)created. Returns true only if they were applied
    /// right now (the handle exists and Windows accepted the backdrop); see <see cref="LastResult"/>
    /// for the outcome of a deferred or repeated application.
    /// </summary>
    public static bool Apply(Form form, BackdropKind backdrop = BackdropKind.Mica, bool? dark = null, bool rounded = true)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!OperatingSystem.IsWindows()) return false;

        bool firstTime = !Wanted.TryGetValue(form, out _);
        Wanted.AddOrUpdate(form, new Settings(backdrop, dark, rounded));
        if (firstTime)
        {
            form.HandleCreated += OnHandleCreated;
            form.Disposed += (_, _) => form.HandleCreated -= OnHandleCreated;
        }
        return form.IsHandleCreated && ApplyNow(form);
    }

    /// <summary>
    /// Re-apply the form's stored settings now — after <see cref="AiFgfTheme.Switch(AiFgfTheme)"/>, so a title bar
    /// that follows the theme (dark = null) turns light or dark with it. False if never applied.
    /// </summary>
    public static bool Refresh(Form form)
        => Wanted.TryGetValue(form, out _) && form.IsHandleCreated && ApplyNow(form);

    /// <summary>The outcome of the most recent application to <paramref name="form"/>, or null if none yet.</summary>
    public static DwmResult? LastResult(Form form) => Results.TryGetValue(form, out var r) ? r : null;

    private static void OnHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Form f) ApplyNow(f);
    }

    private static bool ApplyNow(Form form)
    {
        if (!Wanted.TryGetValue(form, out Settings? s)) return false;
        DwmResult r = ApplyCore(form, s.Backdrop, s.Dark ?? IsDark(AiFgfTheme.Current), s.Rounded);
        int count = Results.TryGetValue(form, out var prev) ? prev.Applications + 1 : 1;
        r = r with { Applications = count };
        Results.AddOrUpdate(form, r);
        return r.Success;
    }

    [SupportedOSPlatform("windows")]
    private static DwmResult ApplyCore(Form form, BackdropKind backdrop, bool dark, bool rounded)
    {
        try
        {
            int darkVal = dark ? 1 : 0;
            int hrDark = DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

            int backdropVal = (int)backdrop;
            int hrBackdrop = DwmSetWindowAttribute(form.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropVal, sizeof(int));

            int corner = rounded ? CORNER_ROUND : CORNER_DONOTROUND;
            int hrCorner = DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            // SYSTEMBACKDROP_TYPE only exists on Windows 11 22H2+; a non-zero HRESULT means "too old".
            return new DwmResult(form.Handle, hrDark, hrBackdrop, hrCorner, corner, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new DwmResult(IntPtr.Zero, -1, -1, -1, 0, 0);
        }
    }

    /// <summary>Dark if the theme's window background is dark (luminance &lt; 50%).</summary>
    public static bool IsDark(AiFgfTheme theme)
    {
        Color c = theme.App;
        double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        return luminance < 128;
    }

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>What <see cref="Dwm.Apply"/> did to a window handle (HRESULTs: 0 = applied).</summary>
public sealed record DwmResult(IntPtr Handle, int DarkModeHResult, int BackdropHResult, int CornerHResult, int CornerPreference, int Applications)
{
    /// <summary>True if Windows accepted the backdrop (i.e. this is Windows 11 22H2+).</summary>
    public bool Success => BackdropHResult == 0;
}
