using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Rectangle = System.Drawing.Rectangle;

namespace AI_FGF.Avalonia;

/// <summary>
/// Settings and presets — the same API and file format as the WinForms <c>AiFgfSettings</c>
/// (shared through <see cref="AiFgfSettingsStore"/>): a preset saved on Windows loads on Linux.
/// </summary>
public static class AiFgfSettings
{
    public static string DefaultPath(string name) => AiFgfSettingsStore.DefaultPath(name);

    public static FormSettingsData Load(string path) => AiFgfSettingsStore.Load(path);

    public static void Save(string path, FormSettingsData data) => AiFgfSettingsStore.Save(path, data);

    public static void SavePreset(string path, FieldSet fields) => Save(path, new FormSettingsData { Fields = fields.GetPersistableValues() });

    public static bool LoadPreset(string path, FieldSet fields) => ToolPresets.Load(path, fields, (_, _) => { });

    /// <summary>The window's placement (device pixels, its DPI) for saving.</summary>
    public static WindowPlacement Capture(Window window)
    {
        double scale = window.RenderScaling;
        return new WindowPlacement(window.Position.X, window.Position.Y,
            (int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale),
            (int)(96 * scale), window.WindowState == WindowState.Maximized);
    }

    /// <summary>Put a saved placement back: rescaled, on a visible screen, within its working area.</summary>
    public static void Restore(Window window, WindowPlacement p)
    {
        double scale = window.RenderScaling;
        var areas = window.Screens.All.Select(s => new Rectangle(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height)).ToList();
        var minimum = new System.Drawing.Size((int)(window.MinWidth * scale), (int)(window.MinHeight * scale));
        Rectangle r = AiFgfSettingsStore.Fit(p, (int)(96 * scale), minimum, areas.Count > 0 ? areas : new List<Rectangle>());
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(r.X, r.Y);
        window.Width = r.Width / scale;
        window.Height = r.Height / scale;
        if (p.Maximized) window.WindowState = WindowState.Maximized;
    }
}
