using System;
using System.Collections.Generic;
using System.Drawing;

namespace AI_FGF;

/// <summary>
/// The colours of a theme, derived from one accent — shared by every front end (WinForms
/// <c>AiFgfTheme.Create</c>, Avalonia <c>AvTheme</c>) so a tool looks the same and stays as readable
/// on Linux as on Windows. Every text/background pair (<see cref="TextPairs"/>) meets
/// <see cref="MinContrast"/>:1; a pale or neon accent is darkened just enough for white text.
/// </summary>
public sealed record AiFgfPalette
{
    /// <summary>Minimum contrast every text/background pair meets (WCAG AA).</summary>
    public const double MinContrast = 4.5;

    /// <summary>Ready-made accent colours (Windows accent palette); names translate via <see cref="AiFgfText.AccentName"/>.</summary>
    public static IReadOnlyList<(string Name, Color Color)> Accents { get; } = new[]
    {
        ("Blue", Color.FromArgb(0, 120, 212)),
        ("Indigo", Color.FromArgb(79, 70, 229)),
        ("Purple", Color.FromArgb(136, 23, 152)),
        ("Pink", Color.FromArgb(191, 0, 119)),
        ("Red", Color.FromArgb(196, 43, 28)),
        ("Orange", Color.FromArgb(202, 80, 16)),
        ("Gold", Color.FromArgb(255, 185, 0)),
        ("Green", Color.FromArgb(16, 124, 16)),
        ("Teal", Color.FromArgb(0, 120, 120)),
        ("Graphite", Color.FromArgb(90, 93, 98)),
    };

    public Color App { get; init; }
    public Color Surface { get; init; }
    public Color Text { get; init; }
    public Color Subtle { get; init; }
    public Color Border { get; init; }
    public Color Accent { get; init; }
    public Color AccentHover { get; init; }
    public Color OnAccent { get; init; } = Color.White;
    public Color Invalid { get; init; }
    public Color HoverFill { get; init; }
    public Color PressFill { get; init; }
    public Color GridHeader { get; init; }
    public Color GridAlt { get; init; }
    public Color GridSelection { get; init; }
    public Color GridLines { get; init; }
    public Color LogBackground { get; init; }
    public Color LogForeground { get; init; }
    public Color LogError { get; init; }
    public Color LogSuccess { get; init; }
    public Color LogWarn { get; init; }
    public Color LogAccent { get; init; }
    public bool Dark { get; init; }
    public AiFgfThemeChoice Choice { get; init; } = null!;

    /// <summary>
    /// The palette for an accent. <paramref name="dark"/> picks the dark variant;
    /// <paramref name="tint"/> (0–1) is how strongly the backgrounds take on the accent's hue.
    /// </summary>
    public static AiFgfPalette Create(Color accent, bool dark = false, double tint = 0.15)
    {
        tint = Math.Clamp(tint, 0, 1);
        var (h, s, _) = AiFgfColor.ToHsl(accent);
        double bgS = s < 0.08 ? 0 : (dark ? 0.04 + 0.22 * tint : 0.05 + 0.45 * tint);   // a grey accent → neutral greys
        Color B(double l, double satFactor = 1) => AiFgfColor.FromHsl(h, bgS * satFactor, l);

        Color app, surface, border, header, alt, lines, textBase, subtleBase;
        if (!dark)
        {
            app = B(0.955 - 0.035 * tint);
            surface = tint < 0.01 ? Color.White : B(1.0 - 0.03 * tint);
            border = B(0.84, 0.8);
            header = B(0.925 - 0.03 * tint);
            alt = B(0.975 - 0.025 * tint);
            lines = B(0.895);
            textBase = AiFgfColor.FromHsl(h, Math.Min(s, 0.3) * 0.4, 0.13);
            subtleBase = AiFgfColor.FromHsl(h, Math.Min(s, 0.3) * 0.4, 0.42);
        }
        else
        {
            app = B(0.125 + 0.02 * tint);
            surface = B(0.21 + 0.02 * tint);   // fields clearly lighter than the window (not dark holes)
            border = B(0.38);                  // field and button edges visible
            header = B(0.175);
            alt = B(0.195 + 0.02 * tint);
            lines = B(0.27);
            surface = AiFgfColor.EnsureContrast(surface, 1.3, app);   // blue hues are dark at the same lightness: keep fields visibly raised
            textBase = AiFgfColor.FromHsl(h, Math.Min(s, 0.3) * 0.3, 0.88);
            subtleBase = AiFgfColor.FromHsl(h, Math.Min(s, 0.3) * 0.3, 0.65);
        }

        // the accent carries white text (primary button): darken it just enough
        Color acc = AiFgfColor.EnsureContrast(accent, MinContrast, Color.White);
        var (ah, aS, aL) = AiFgfColor.ToHsl(acc);
        Color accHover = AiFgfColor.FromHsl(ah, aS, aL * 0.86);

        Color hover = AiFgfColor.Mix(surface, acc, dark ? 0.16 : 0.08);
        Color press = AiFgfColor.Mix(surface, acc, dark ? 0.26 : 0.16);
        Color selection = AiFgfColor.Mix(surface, acc, dark ? 0.42 : 0.22);
        Color invalid = AiFgfColor.Mix(surface, Color.FromArgb(232, 17, 35), dark ? 0.30 : 0.12);

        Color text = AiFgfColor.EnsureContrast(textBase, 7.0, app, surface);
        // backgrounds that text sits on: pull any that is still too close toward the surface
        Color Fix(Color back) { for (int i = 0; i < 20 && AiFgfColor.Contrast(text, back) < MinContrast; i++) back = AiFgfColor.Mix(back, surface, 0.15); return back; }
        hover = Fix(hover); press = Fix(press); selection = Fix(selection); invalid = Fix(invalid); header = Fix(header); alt = Fix(alt);
        Color subtle = AiFgfColor.EnsureContrast(subtleBase, MinContrast, app, surface);
        border = AiFgfColor.EnsureContrast(border, 1.4, surface);   // field and button edges stay visible (yellow hues are bright at the same lightness)

        // the log follows the theme: a near-white page in a light window, dark grey (never black) in a dark one
        Color logBack = dark ? B(0.16 + 0.02 * tint) : (tint < 0.01 ? Color.FromArgb(251, 251, 252) : B(0.99 - 0.02 * tint));
        Color LogColour(Color c) => AiFgfColor.EnsureContrast(c, MinContrast, logBack);
        Color logAccent = LogColour(AiFgfColor.FromHsl(h, Math.Max(s, 0.45) * (s < 0.08 ? 0 : 1), dark ? 0.65 : 0.38));
        Color logError = LogColour(dark ? Color.FromArgb(255, 138, 128) : Color.FromArgb(180, 35, 24));
        Color logSuccess = LogColour(dark ? Color.FromArgb(110, 205, 130) : Color.FromArgb(16, 112, 16));
        Color logWarn = LogColour(dark ? Color.FromArgb(240, 170, 60) : Color.FromArgb(140, 84, 0));
        Color logFore = AiFgfColor.EnsureContrast(text, 7.0, logBack);

        return new AiFgfPalette
        {
            App = app, Surface = surface, Text = text, Subtle = subtle, Border = border,
            Accent = acc, AccentHover = accHover, OnAccent = Color.White,
            Invalid = invalid, HoverFill = hover, PressFill = press,
            GridHeader = header, GridAlt = alt, GridSelection = selection, GridLines = lines,
            LogBackground = logBack, LogForeground = logFore,
            LogAccent = logAccent, LogError = logError, LogSuccess = logSuccess, LogWarn = logWarn,
            Dark = dark,
            Choice = new AiFgfThemeChoice(accent, dark, tint),
        };
    }

    /// <summary>Every text colour on every background it is drawn on — what must stay readable.</summary>
    public IEnumerable<(string What, Color Fore, Color Back)> TextPairs()
    {
        yield return ("Text on App", Text, App);
        yield return ("Text on Surface", Text, Surface);
        yield return ("Subtle on App", Subtle, App);
        yield return ("Subtle on Surface", Subtle, Surface);
        yield return ("OnAccent on Accent", OnAccent, Accent);
        yield return ("OnAccent on AccentHover", OnAccent, AccentHover);
        yield return ("Text on Invalid", Text, Invalid);
        yield return ("Text on HoverFill", Text, HoverFill);
        yield return ("Text on PressFill", Text, PressFill);
        yield return ("Text on GridHeader", Text, GridHeader);
        yield return ("Text on GridAlt", Text, GridAlt);
        yield return ("Text on GridSelection", Text, GridSelection);
        yield return ("LogForeground", LogForeground, LogBackground);
        yield return ("LogError", LogError, LogBackground);
        yield return ("LogSuccess", LogSuccess, LogBackground);
        yield return ("LogWarn", LogWarn, LogBackground);
        yield return ("LogAccent", LogAccent, LogBackground);
    }
}
