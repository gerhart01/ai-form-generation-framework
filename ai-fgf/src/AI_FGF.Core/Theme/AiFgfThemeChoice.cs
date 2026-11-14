using System;
using System.Drawing;
using System.Globalization;

namespace AI_FGF;

/// <summary>
/// A user's colour choice: an accent colour, light or dark, and how strongly the backgrounds
/// take on the accent's hue (0 = neutral grey, 1 = clearly tinted). Each front end turns it into
/// its own theme (WinForms: <c>choice.ToTheme()</c>).
/// </summary>
public sealed record AiFgfThemeChoice(Color Accent, bool Dark, double Tint)
{
    /// <summary>"#0078D4;light;0.15" — what settings files store.</summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{AiFgfColor.ToHex(Accent)};{(Dark ? "dark" : "light")};{Tint:0.##}");

    public static bool TryParse(string? text, out AiFgfThemeChoice choice)
    {
        choice = null!;
        string[] p = (text ?? "").Split(';');
        if (p.Length != 3 || !AiFgfColor.TryParseHex(p[0], out Color accent)) return false;
        bool dark = p[1].Trim().Equals("dark", StringComparison.OrdinalIgnoreCase);
        if (!dark && !p[1].Trim().Equals("light", StringComparison.OrdinalIgnoreCase)) return false;
        if (!double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double tint) || tint is < 0 or > 1) return false;
        choice = new AiFgfThemeChoice(accent, dark, tint);
        return true;
    }

    // records compare Color by name; compare by value
    public bool Equals(AiFgfThemeChoice? other)
        => other is not null && Accent.ToArgb() == other.Accent.ToArgb() && Dark == other.Dark && Math.Abs(Tint - other.Tint) < 0.005;

    public override int GetHashCode() => HashCode.Combine(Accent.ToArgb(), Dark, Math.Round(Tint, 2));
}
