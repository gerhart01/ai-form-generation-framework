using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// Native dark rendering for the parts WinForms leaves to the system: a drop-down list's face,
/// scroll bars of multi-line boxes, logs and grids. Without it a dark window shows white
/// combo boxes and light scroll bars. In a dark theme each window is themed with Windows' own dark
/// control themes (<c>DarkMode_CFD</c>, <c>DarkMode_Explorer</c>); in a light theme the override is
/// removed. Applied as handles are created and again on <see cref="AiFgfTheme.Switch(AiFgfTheme)"/>.
/// </summary>
public static class AiFgfNativeTheme
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    private const int WM_THEMECHANGED = 0x031A;

    /// <summary>Theme <paramref name="root"/> and its children now and as their handles are created or they are added.</summary>
    public static void Attach(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.HandleCreated -= OnHandleCreated;
        root.HandleCreated += OnHandleCreated;
        root.ControlAdded -= OnControlAdded;
        root.ControlAdded += OnControlAdded;
        if (root.IsHandleCreated) ApplyOne(root);
        foreach (Control child in root.Controls) Attach(child);
    }

    /// <summary>Re-apply to a tree after the theme changed.</summary>
    public static void Refresh(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.IsHandleCreated) ApplyOne(root);
        foreach (Control child in root.Controls) Refresh(child);
    }

    private static void OnHandleCreated(object? sender, EventArgs e) { if (sender is Control c) ApplyOne(c); }

    private static void OnControlAdded(object? sender, ControlEventArgs e) { if (e.Control is { } c) Attach(c); }

    /// <summary>The control theme to use for <paramref name="c"/> in a dark theme, or null if it needs none.</summary>
    private static string? DarkThemeFor(Control c) => c switch
    {
        ComboBox => "DarkMode_CFD",                                  // the drop-down face and list
        TextBoxBase { Multiline: true } => "DarkMode_Explorer",      // scroll bars of memos and logs
        // the parameters' scroller turns AutoScroll on only later (when the window gets too short)
        DataGridView or ParamScroller or ScrollableControl { AutoScroll: true } => "DarkMode_Explorer",
        _ => null,
    };

    private static void ApplyOne(Control c)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) || DarkThemeFor(c) is not { } dark) return;
        string? theme = Dwm.IsDark(AiFgfTheme.Current) ? dark : null;
        try
        {
            SetWindowTheme(c.Handle, theme, null);
            // DataGridView and a scrolling Panel/ParamScroller draw their own scroll bars as separate
            // native "ScrollBar" child windows (unlike a themed comctl control, which themes its own
            // built-in scroll bar when its own handle is themed) - SetWindowTheme on the parent alone
            // never reaches them, leaving a light scroll bar in an otherwise dark window.
            if (c is DataGridView or ScrollableControl) ThemeScrollbarChildren(c.Handle, theme);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { /* no uxtheme: system look */ }
    }

    private static void ThemeScrollbarChildren(IntPtr parent, string? theme)
    {
        var name = new StringBuilder(64);
        EnumChildWindows(parent, (hwnd, _) =>
        {
            name.Clear();
            GetClassName(hwnd, name, name.Capacity);
            // WinForms wraps the native scroll bar in its own registered window class -
            // "WindowsForms10.ScrollBar.app.0.<hash>" - never the bare Win32 "ScrollBar".
            if (name.ToString().Contains("ScrollBar", StringComparison.OrdinalIgnoreCase))
            {
                SetWindowTheme(hwnd, theme, null);
                SendMessage(hwnd, WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
    }
}
