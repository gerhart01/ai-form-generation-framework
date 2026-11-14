using System.Drawing;

namespace AI_FGF;

/// <summary>
/// A named colour + font palette shared by every AI_FGF control and helper.
///
/// The hand-coded GUIs this framework is distilled from each re-declared the same
/// <c>ClrApp / ClrSurface / ClrText / ClrAccent …</c> constants at the top of their
/// main form. AI_FGF lifts that into one swappable object: set
/// <see cref="AiFgfTheme.Current"/> once at start-up and the whole UI follows.
/// </summary>
public sealed partial class AiFgfTheme
{
    // ---- surfaces & text -------------------------------------------------
    public Color App { get; init; } = Color.FromArgb(243, 244, 246);   // window background
    public Color Surface { get; init; } = Color.White;                 // inputs, cards, grids
    public Color Text { get; init; } = Color.FromArgb(30, 33, 38);
    public Color Subtle { get; init; } = Color.FromArgb(103, 110, 120);
    public Color Border { get; init; } = Color.FromArgb(210, 214, 220);

    // ---- accent (primary action) ----------------------------------------
    public Color Accent { get; init; } = Color.FromArgb(0, 120, 212);
    public Color AccentHover { get; init; } = Color.FromArgb(0, 103, 184);
    public Color OnAccent { get; init; } = Color.White;

    // ---- validation -------------------------------------------------------
    public Color Invalid { get; init; } = Color.FromArgb(253, 231, 233);   // tint of a field that failed validation

    // ---- button hover/press (neutral buttons) ---------------------------
    public Color HoverFill { get; init; } = Color.FromArgb(234, 240, 247);
    public Color PressFill { get; init; } = Color.FromArgb(222, 231, 241);

    // ---- data grid -------------------------------------------------------
    public Color GridHeader { get; init; } = Color.FromArgb(236, 239, 243);
    public Color GridAlt { get; init; } = Color.FromArgb(247, 249, 251);
    public Color GridSelection { get; init; } = Color.FromArgb(208, 228, 246);
    public Color GridLines { get; init; } = Color.FromArgb(230, 233, 237);

    // ---- output log: follows the theme (a near-black console in a light window looked like a hole)
    public Color LogBackground { get; init; } = Color.FromArgb(251, 251, 252);
    public Color LogForeground { get; init; } = Color.FromArgb(30, 33, 38);
    public Color LogError { get; init; } = Color.FromArgb(180, 35, 24);
    public Color LogSuccess { get; init; } = Color.FromArgb(16, 112, 16);
    public Color LogWarn { get; init; } = Color.FromArgb(140, 84, 0);
    public Color LogAccent { get; init; } = Color.FromArgb(0, 95, 184);

    // ---- fonts -----------------------------------------------------------
    public string UiFontFamily { get; init; } = "Segoe UI";
    public float UiFontSize { get; init; } = 9.25f;
    public string MonoFontFamily { get; init; } = "Consolas";
    public float MonoFontSize { get; init; } = 9.5f;

    public Font UiFont() => new(UiFontFamily, UiFontSize);
    public Font UiFontBold() => new(UiFontFamily, UiFontSize, FontStyle.Bold);
    public Font HeaderFont() => new($"{UiFontFamily} Semibold", UiFontSize);
    public Font MonoFont() => new(MonoFontFamily, MonoFontSize);

    /// <summary>The calm light theme of the hand-coded tool GUIs AI_FGF grew from.</summary>
    public static AiFgfTheme Light { get; } = new();

    /// <summary>A dark variant that reuses the console colours for the whole shell.</summary>
    public static AiFgfTheme Dark { get; } = new()
    {
        App = Color.FromArgb(32, 33, 36),
        Surface = Color.FromArgb(52, 54, 59),      // fields clearly lighter than the window, not dark holes
        Text = Color.FromArgb(228, 230, 233),
        Subtle = Color.FromArgb(160, 165, 172),
        Border = Color.FromArgb(92, 96, 103),      // field and button edges visible
        HoverFill = Color.FromArgb(64, 67, 73),
        PressFill = Color.FromArgb(76, 80, 87),
        GridHeader = Color.FromArgb(44, 46, 50),
        GridAlt = Color.FromArgb(47, 49, 54),
        GridSelection = Color.FromArgb(0, 90, 158),
        GridLines = Color.FromArgb(66, 69, 75),
        Invalid = Color.FromArgb(96, 38, 44),
        LogBackground = Color.FromArgb(40, 42, 46),   // dark grey, between window and fields — not black
        LogForeground = Color.FromArgb(228, 230, 233),
        LogError = Color.FromArgb(255, 138, 128),
        LogSuccess = Color.FromArgb(110, 205, 130),
        LogWarn = Color.FromArgb(240, 170, 60),
        LogAccent = Color.FromArgb(110, 170, 230),
    };

    /// <summary>A dark, blue-tinted variant (Create's validated palette maths, not hand-picked), for apps that want a "midnight" look.</summary>
    public static AiFgfTheme MidnightBlue { get; } = Create(Color.FromArgb(64, 140, 255), dark: true, tint: 0.35);

    /// <summary>
    /// The active theme. Assign before building any form (e.g. in <c>Main</c>) to restyle everything.
    /// </summary>
    public static AiFgfTheme Current { get; set; } = Light;
}
