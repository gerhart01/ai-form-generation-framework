using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// JSON persistence for window placement, field values and presets — the reference projects
/// each rolled their own (Settings.settings, config.json, presets/*.json).
///
/// Restoring placement handles the classic bugs: a window saved on a monitor that is now
/// gone (moved back onto a visible screen), saved at a different DPI (size rescaled), or
/// larger than the current screen (clamped to the working area).
/// </summary>
public static class AiFgfSettings
{
    /// <inheritdoc cref="AiFgfSettingsStore.DefaultPath"/>
    public static string DefaultPath(string name) => AiFgfSettingsStore.DefaultPath(name);

    /// <inheritdoc cref="AiFgfSettingsStore.Load"/>
    public static FormSettingsData Load(string path) => AiFgfSettingsStore.Load(path);

    /// <inheritdoc cref="AiFgfSettingsStore.Save"/>
    public static void Save(string path, FormSettingsData data) => AiFgfSettingsStore.Save(path, data);

    // -------------------------------------------------------------- presets

    /// <summary>Write field values to a preset file.</summary>
    public static void SavePreset(string path, FieldSet fields)
        => Save(path, new FormSettingsData { Fields = fields.GetValues() });

    /// <summary>Apply a preset file's values to the fields. Returns false if the file had none.</summary>
    public static bool LoadPreset(string path, FieldSet fields)
    {
        var data = Load(path);
        if (data.Fields.Count == 0) return false;
        fields.SetValues(data.Fields);
        return true;
    }

    // ------------------------------------------------------ window placement

    /// <summary>
    /// Capture a form's placement: its normal bounds, and whether it is (or, if minimized,
    /// will restore to) maximized — a window maximized, then minimized, then closed from the
    /// taskbar should come back maximized.
    /// </summary>
    public static WindowPlacement Capture(Form form)
    {
        Rectangle b = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
        bool maximized = form.WindowState == FormWindowState.Maximized
                         || (form.WindowState == FormWindowState.Minimized && RestoresToMaximized(form));
        return new WindowPlacement(b.X, b.Y, b.Width, b.Height, form.DeviceDpi, maximized);
    }

    private static bool RestoresToMaximized(Form form)
    {
        if (!form.IsHandleCreated) return false;
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        return GetWindowPlacement(form.Handle, ref wp) && (wp.flags & WPF_RESTORETOMAXIMIZED) != 0;
    }

    private const int WPF_RESTORETOMAXIMIZED = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public Point ptMinPosition, ptMaxPosition;
        public Rectangle rcNormalPosition; // RECT is 4 ints, same layout as these 4 ints
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

    /// <inheritdoc cref="AiFgfSettingsStore.Fit"/>
    public static Rectangle Fit(WindowPlacement p, int currentDpi, Size minimum, IReadOnlyList<Rectangle> workingAreas)
        => AiFgfSettingsStore.Fit(p, currentDpi, minimum, workingAreas);

    /// <summary>Restore a saved placement onto <paramref name="form"/> (call before it is shown).</summary>
    public static void Restore(Form form, WindowPlacement p)
    {
        var areas = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
        Rectangle r = Fit(p, form.DeviceDpi, form.MinimumSize, areas);
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = r;
        if (p.Maximized) form.WindowState = FormWindowState.Maximized;
    }

}
