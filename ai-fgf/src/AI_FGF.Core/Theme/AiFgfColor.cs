using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace AI_FGF;

/// <summary>Colour arithmetic for themes: HSL, mixing, WCAG contrast and contrast repair.</summary>
public static class AiFgfColor
{
    /// <summary>WCAG 2 relative luminance (0 = black, 1 = white).</summary>
    public static double Luminance(Color c)
    {
        static double Ch(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    /// <summary>WCAG 2 contrast ratio of two colours (1 to 21).</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary><paramref name="a"/> blended toward <paramref name="b"/> by <paramref name="t"/> (0 = a, 1 = b).</summary>
    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Hue (0–360), saturation and lightness (0–1).</summary>
    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360; s = Math.Clamp(s, 0, 1); l = Math.Clamp(l, 0, 1);
        if (s < 1e-9) { int v = (int)Math.Round(l * 255); return Color.FromArgb(v, v, v); }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        return Color.FromArgb((int)Math.Round(Hue(p, q, h + 1.0 / 3) * 255), (int)Math.Round(Hue(p, q, h) * 255), (int)Math.Round(Hue(p, q, h - 1.0 / 3) * 255));
    }

    /// <summary>
    /// <paramref name="fore"/>, made lighter or darker (keeping its hue) just enough to reach
    /// <paramref name="min"/>:1 against every one of <paramref name="backs"/>. It moves away from
    /// the backgrounds' luminance — toward white on dark backgrounds, toward black on light ones.
    /// </summary>
    public static Color EnsureContrast(Color fore, double min, params Color[] backs)
    {
        if (backs.Length == 0 || backs.All(b => Contrast(fore, b) >= min)) return fore;
        bool lighten = backs.Average(Luminance) < 0.18;   // ~ the luminance where black and white text contrast equally
        var (h, s, l) = ToHsl(fore);
        for (int i = 1; i <= 100; i++)
        {
            Color c = FromHsl(h, s, lighten ? l + (1 - l) * i / 100.0 : l * (1 - i / 100.0));
            if (backs.All(b => Contrast(c, b) >= min)) return c;
        }
        return lighten ? Color.White : Color.Black;
    }

    /// <summary>"#RRGGBB".</summary>
    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Parse "#RRGGBB", "RRGGBB" or "#RGB"; false for anything else.</summary>
    public static bool TryParseHex(string? text, out Color color)
    {
        color = Color.Empty;
        string s = (text ?? "").Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(ch => $"{ch}{ch}"));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return false;
        color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }
}
