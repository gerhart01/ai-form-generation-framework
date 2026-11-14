using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI_FGF;

/// <summary>One visible element of a window: what it is, what it says, where it is (logical px, from the client's top-left) and its font.</summary>
public sealed record AiFgfElement(string Kind, string Text, double X, double Y, double Width, double Height,
                               string Font = "", double FontPt = 0, bool Bold = false)
{
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Kind} '{Text}' @{X:0},{Y:0} {Width:0}x{Height:0}{(Font.Length > 0 ? $" {Font} {FontPt:0.##}pt{(Bold ? " bold" : "")}" : "")}");
}

/// <summary>
/// How close two snapshots must be for <see cref="AiFgfLayoutSnapshot.Compare"/>: positions and sizes in
/// logical px, sizes also by <c>SizeRelative</c> of their length (text renderers differ by ~2% on long captions).
/// <c>IgnoreExtra</c>: report only what the reference has and this one lacks. <c>LooseText</c>: captions match
/// without a leading glyph (▶, ■), a trailing ":" or " *", and with "..." as "…".
/// </summary>
public sealed record AiFgfSnapshotTolerance(double Position = 3, double Size = 3, double FontPt = 0.1, bool CheckFonts = true, bool CheckGeometry = true,
                                         double SizeRelative = 0.02, bool IgnoreExtra = false, bool LooseText = false)
{
    public static AiFgfSnapshotTolerance Default { get; } = new();

    /// <summary>
    /// A form moved to AI_FGF: every caption and input of the old form (the reference) must still be
    /// there; layout, fonts and the new form's extra parts (Run, log, status…) are not compared.
    /// </summary>
    public static AiFgfSnapshotTolerance ContentOnly { get; } = new(CheckFonts: false, CheckGeometry: false, IgnoreExtra: true, LooseText: true);
}

/// <summary>
/// A toolkit-neutral picture of a window — every visible label, button, field, tab, log… with its
/// text, logical bounds and actual font. The WinForms and Avalonia front ends both take one
/// (<c>AiFgfSnapshot.Of(form)</c> on both), so a form can be compared
/// across toolkits (the Avalonia window against the WinForms one), against a saved golden file,
/// or an existing hand-made form against its AI_FGF replacement: <see cref="Compare"/> lists
/// what is missing, extra, moved, resized or in another font.
/// </summary>
public sealed class AiFgfLayoutSnapshot
{
    /// <summary>Element kinds the front ends emit.</summary>
    public static class Kinds
    {
        public const string Group = "Group", Label = "Label", Button = "Button", Check = "Check", Text = "Text", Number = "Number",
                            Combo = "Combo", Tab = "Tab", Log = "Log", Grid = "Grid", Progress = "Progress", Status = "Status";
    }

    public string Toolkit { get; set; } = "";
    /// <summary>Client size, logical px.</summary>
    public double Width { get; set; }
    public double Height { get; set; }
    public List<AiFgfElement> Elements { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AiFgfLayoutSnapshot FromJson(string json) => JsonSerializer.Deserialize<AiFgfLayoutSnapshot>(json, Json) ?? new AiFgfLayoutSnapshot();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToJson());
    }

    public static AiFgfLayoutSnapshot Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>The caption as shown: mnemonic markers removed ("&amp;Run" / "_Run" → "Run", doubled ones kept once).</summary>
    public static string Plain(string? text, char marker)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == marker) { if (i + 1 < text.Length && text[i + 1] == marker) { sb.Append(marker); i++; } continue; }
            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    /// <summary>Round to 0.1 px so files stay readable and diffs stable.</summary>
    public static double R(double v) => Math.Round(v, 1);

    /// <summary>
    /// The differences from <paramref name="reference"/>, one line each (empty: the same). Elements
    /// are paired by kind and text; elements without text (inputs) by kind and reading order.
    /// </summary>
    public IReadOnlyList<string> Compare(AiFgfLayoutSnapshot reference, AiFgfSnapshotTolerance? tolerance = null)
    {
        AiFgfSnapshotTolerance t = tolerance ?? AiFgfSnapshotTolerance.Default;
        var issues = new List<string>();
        Func<string, string> text = t.LooseText ? Loose : s => s;
        foreach (var group in Elements.Concat(reference.Elements).Select(e => (e.Kind, Text: text(e.Text))).Distinct())
        {
            List<AiFgfElement> mine = Ordered(Elements, group, text), theirs = Ordered(reference.Elements, group, text);
            for (int i = 0; i < Math.Max(mine.Count, theirs.Count); i++)
            {
                if (i >= mine.Count) { issues.Add($"missing: {theirs[i]}"); continue; }
                if (i >= theirs.Count) { if (!t.IgnoreExtra) issues.Add($"extra: {mine[i]}"); continue; }
                AiFgfElement a = mine[i], b = theirs[i];
                string name = $"{a.Kind} '{a.Text}'" + (theirs.Count > 1 ? $" #{i + 1}" : "");
                if (t.CheckFonts && a.Font.Length > 0 && b.Font.Length > 0)
                {
                    if (!string.Equals(a.Font, b.Font, StringComparison.OrdinalIgnoreCase)) issues.Add($"{name}: font {a.Font}, expected {b.Font}");
                    if (Math.Abs(a.FontPt - b.FontPt) > t.FontPt) issues.Add(F($"{name}: font size {a.FontPt:0.##}pt, expected {b.FontPt:0.##}pt"));
                    if (a.Bold != b.Bold) issues.Add($"{name}: {(a.Bold ? "bold" : "regular")}, expected {(b.Bold ? "bold" : "regular")}");
                }
                if (t.CheckGeometry)
                {
                    if (Math.Abs(a.X - b.X) > t.Position || Math.Abs(a.Y - b.Y) > t.Position) issues.Add(F($"{name}: at {a.X:0},{a.Y:0}, expected {b.X:0},{b.Y:0}"));
                    if (Math.Abs(a.Width - b.Width) > Math.Max(t.Size, b.Width * t.SizeRelative)
                        || Math.Abs(a.Height - b.Height) > Math.Max(t.Size, b.Height * t.SizeRelative)) issues.Add(F($"{name}: {a.Width:0}x{a.Height:0}, expected {b.Width:0}x{b.Height:0}"));
                }
            }
        }
        return issues;
    }

    private static List<AiFgfElement> Ordered(IEnumerable<AiFgfElement> all, (string Kind, string Text) key, Func<string, string> text)
        => all.Where(e => e.Kind == key.Kind && text(e.Text) == key.Text).OrderBy(e => Math.Round(e.Y / 4)).ThenBy(e => e.X).ToList();

    /// <summary>"▶ Run" ~ "Run", "Source:" ~ "Source *" ~ "Source", "Browse..." ~ "Browse…".</summary>
    private static string Loose(string s)
    {
        s = s.Replace("...", "…").Trim().TrimEnd('*').TrimEnd().TrimEnd(':').Trim();
        int start = 0;
        while (start < s.Length && !char.IsLetterOrDigit(s[start])) start++;   // a leading glyph: ▶, ■, ✓
        return start > 0 && start < s.Length ? s[start..].TrimStart() : s;
    }

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
