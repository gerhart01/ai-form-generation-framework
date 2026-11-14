using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AI_FGF;

/// <summary>A window's saved placement, in device pixels at the DPI it was saved at.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, int Dpi, bool Maximized);

/// <summary>What a front end's <c>AiFgfSettings</c> persists for one form.</summary>
public sealed class FormSettingsData
{
    public WindowPlacement? Window { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The colour choice (<see cref="AiFgfThemeChoice.ToString"/>), when the user picked one.</summary>
    public string? Theme { get; set; }
    /// <summary>The button style the user picked (<see cref="AiFgfEffects.Name"/>), when they picked one.</summary>
    public string? Buttons { get; set; }
}

/// <summary>
/// The UI-independent half of settings persistence, shared by the WinForms and Avalonia
/// front ends: the file location, atomic JSON load/save, and fitting a saved placement onto
/// the screens there are now. The same file format on Windows and Linux.
/// </summary>
public static class AiFgfSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary><c>%AppData%\AI_FGF\{name}.json</c> on Windows, <c>~/.config/AI_FGF/{name}.json</c> on Linux.</summary>
    public static string DefaultPath(string name)
    {
        string safe = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' or ':' ? '_' : ch));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AI_FGF", safe + ".json");
    }

    /// <summary>Load settings; a missing or corrupt file yields empty settings (never throws).</summary>
    public static FormSettingsData Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new FormSettingsData();
            var data = JsonSerializer.Deserialize<FormSettingsData>(File.ReadAllText(path), Json);
            if (data is null) return new FormSettingsData();
            // The deserialised dictionary is ordinal, so a hand-edited file may hold "Port" and
            // "port" side by side; copying with the constructor would throw. Last one wins.
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in data.Fields ?? new()) if (key is not null && value is not null) fields[key] = value;
            data.Fields = fields;
            return data;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            return new FormSettingsData();
        }
    }

    /// <summary>
    /// Save settings atomically (write to a temp file, then replace). Relative paths are resolved
    /// against the current directory. On failure the temp file is removed and the exception
    /// propagates (callers decide whether it matters).
    /// </summary>
    public static void Save(string path, FormSettingsData data)
    {
        path = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Where a saved placement should go now: rescaled to the current DPI, clamped to the
    /// working area, and moved onto a visible screen if its old monitor is gone.
    /// Respects <paramref name="minimum"/>.
    /// </summary>
    public static Rectangle Fit(WindowPlacement p, int currentDpi, Size minimum, IReadOnlyList<Rectangle> workingAreas)
    {
        double k = p.Dpi > 0 ? currentDpi / (double)p.Dpi : 1.0;
        int w = Math.Max(minimum.Width, (int)Math.Round(p.Width * k));
        int h = Math.Max(minimum.Height, (int)Math.Round(p.Height * k));
        var saved = new Rectangle(p.X, p.Y, w, h);

        // Which screen should host it: the one showing most of the title bar, else the primary.
        Rectangle titleBar = new(saved.X, saved.Y, saved.Width, Math.Min(saved.Height, 40));
        Rectangle area = workingAreas
            .Select(a => (a, overlap: Area(Rectangle.Intersect(a, titleBar))))
            .Where(t => t.overlap >= 40 * 20)            // at least a grabbable chunk of title bar
            .OrderByDescending(t => t.overlap)
            .Select(t => t.a)
            .DefaultIfEmpty(workingAreas.Count > 0 ? workingAreas[0] : new Rectangle(0, 0, 1024, 768))
            .First();

        w = Math.Min(w, Math.Max(minimum.Width, area.Width));
        h = Math.Min(h, Math.Max(minimum.Height, area.Height));
        bool visible = Area(Rectangle.Intersect(area, titleBar)) >= 40 * 20;
        int x = visible ? saved.X : area.X + (area.Width - w) / 2;
        int y = visible ? saved.Y : area.Y + (area.Height - h) / 2;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - w));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - h));
        return new Rectangle(x, y, w, h);
    }

    private static long Area(Rectangle r) => r.IsEmpty ? 0 : (long)r.Width * r.Height;
}
