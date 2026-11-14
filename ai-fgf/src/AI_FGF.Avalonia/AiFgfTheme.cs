using System;
using System.Collections.Generic;
using Avalonia;
using Color = System.Drawing.Color;

namespace AI_FGF.Avalonia;

/// <summary>
/// The colours of the Avalonia front end — the same API as the WinForms <c>AiFgfTheme</c>
/// (<see cref="Light"/>, <see cref="Dark"/>, <see cref="Create"/>, <see cref="Current"/>,
/// <see cref="Switch"/>, the colour roles) over the shared <see cref="AiFgfPalette"/>, so theme code
/// is the same on both and a palette reads the same on Linux as on Windows.
/// </summary>
public sealed class AiFgfTheme
{
    private AiFgfTheme(AiFgfPalette palette) => Palette = palette;

    /// <summary>The colours.</summary>
    public AiFgfPalette Palette { get; }

    public Color App => Palette.App;
    public Color Surface => Palette.Surface;
    public Color Text => Palette.Text;
    public Color Subtle => Palette.Subtle;
    public Color Border => Palette.Border;
    public Color Accent => Palette.Accent;
    public Color AccentHover => Palette.AccentHover;
    public Color OnAccent => Palette.OnAccent;
    public Color Invalid => Palette.Invalid;
    public Color HoverFill => Palette.HoverFill;
    public Color PressFill => Palette.PressFill;
    public Color GridHeader => Palette.GridHeader;
    public Color GridAlt => Palette.GridAlt;
    public Color GridSelection => Palette.GridSelection;
    public Color GridLines => Palette.GridLines;
    public Color LogBackground => Palette.LogBackground;
    public Color LogForeground => Palette.LogForeground;
    public Color LogError => Palette.LogError;
    public Color LogSuccess => Palette.LogSuccess;
    public Color LogWarn => Palette.LogWarn;
    public Color LogAccent => Palette.LogAccent;

    /// <summary>The choice this theme was built from.</summary>
    public AiFgfThemeChoice Choice => Palette.Choice;

    public bool IsDark => Palette.Dark;

    public const double MinContrast = AiFgfPalette.MinContrast;

    public static IReadOnlyList<(string Name, Color Color)> Accents => AiFgfPalette.Accents;

    public static AiFgfTheme Light { get; } = Create(Color.FromArgb(0, 120, 212), false, 0);
    public static AiFgfTheme Dark { get; } = Create(Color.FromArgb(0, 120, 212), true, 0);
    /// <summary>A dark, blue-tinted variant, for apps that want a "midnight" look (same accent/tint as the WinForms one).</summary>
    public static AiFgfTheme MidnightBlue { get; } = Create(Color.FromArgb(64, 140, 255), true, 0.35);

    /// <summary>A complete, readable theme from one accent (see <see cref="AiFgfPalette.Create"/>).</summary>
    public static AiFgfTheme Create(Color accent, bool dark = false, double tint = 0.15) => new(AiFgfPalette.Create(accent, dark, tint));

    /// <summary>Every text colour on every background it is drawn on.</summary>
    public IEnumerable<(string What, Color Fore, Color Back)> TextPairs() => Palette.TextPairs();

    private static AiFgfTheme _current = Light;

    /// <summary>The active theme; assigning it is <see cref="Switch"/>.</summary>
    public static AiFgfTheme Current { get => _current; set => Switch(value); }

    /// <summary>Raised after the theme changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>Make <paramref name="theme"/> current; open windows follow at once (their colours are theme resources).</summary>
    public static void Switch(AiFgfTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _current = theme;
        if (Application.Current is { } app) AvTheme.Apply(app, theme.Palette);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>Avalonia side of <see cref="AiFgfThemeChoice"/>.</summary>
public static class AiFgfThemeChoiceExtensions
{
    public static AiFgfTheme ToTheme(this AiFgfThemeChoice choice) => AiFgfTheme.Create(choice.Accent, choice.Dark, choice.Tint);
}
