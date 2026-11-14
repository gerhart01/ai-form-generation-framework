using Avalonia;
using Avalonia.Media;

namespace AI_FGF.Avalonia;

/// <summary>
/// The WinForms front end's spacing, in logical px, so a AI_FGF.Avalonia window is laid out like the
/// AI_FGF WinForms form built from the same code. Each value names its WinForms origin; the
/// golden-snapshot parity test (tests/Shared/Golden) holds the two within a few pixels.
/// Text itself measures the same on both (Skia and GDI widths agree within ~2%), but WinForms sizes
/// labels, buttons and tabs with TextRenderer, which pads text by about a third of the font size on
/// each side — <see cref="TextPad"/> adds that padding here.
/// </summary>
public static class WinFormsMetrics
{
    /// <summary>TextRenderer's padding on each side of a measured text, as a fraction of the font size (4 px at 9.25 pt).</summary>
    public const double TextPadEm = 0.325;

    public static double TextPad(double fontSize) => Math.Round(fontSize * TextPadEm, 1);

    /// <summary>FormScaffold: form.Padding.</summary>
    public static readonly Thickness FormPadding = new(10, 10, 10, 0);

    /// <summary>RowBuilder.MakeLabel: Margin(3, 3, 8, 3).</summary>
    public static readonly Thickness LabelMargin = new(3, 3, 8, 3);

    /// <summary>AiFgf.Text / Number / Combo: Margin(3, 4, 3, 4) — symmetric so the field centres on its label at every DPI.</summary>
    public static readonly Thickness InputMargin = new(3, 4, 3, 4);

    /// <summary>RowBuilder.Checks: the check-box row, Margin(0, 2, 0, 2).</summary>
    public static readonly Thickness ChecksRowMargin = new(0, 2, 0, 2);

    /// <summary>AiFgf.Check: Margin(0, 6, 16, 0).</summary>
    public static readonly Thickness CheckMargin = new(0, 6, 16, 0);

    /// <summary>The check box glyph (13 px at 96 DPI), the space between it and the caption, and the space after the caption (228 px for a 202-px caption).</summary>
    public const double CheckGlyph = 13, CheckGap = 5.6, CheckTrail = 7.2;

    /// <summary>The up/down arrows of a NumericUpDown: one stacked column this wide.</summary>
    public const double SpinWidth = 16;

    /// <summary>The parameters GroupBox, Padding(12, 6, 12, 10), with its caption band and border.</summary>
    public static readonly Thickness GroupPadding = new(11.2, 14, 11.2, 8.4);

    /// <summary>A text box / spinner: the edit control's border and inner margin around one line of text (22.4 px tall at 9.25 pt).</summary>
    public static readonly Thickness EditPadding = new(1, 1.7, 1, 1.7);

    /// <summary>AiFgf.Button Padding(12, 6) and AiFgf.SmallButton Padding(8, 4): AiFgfButton adds 8 px to the preferred size, TextRenderer its text padding.</summary>
    public static Thickness ButtonPadding(double winFormsH, double winFormsV, double fontSize)
        => new(winFormsH + 4 + TextPad(fontSize), winFormsV + 4);

    /// <summary>ToolForm: the progress panel (Height 44, Padding(8, 2, 8, 4)) with an 18-px bar.</summary>
    public static readonly Thickness ProgressPadding = new(8, 2, 8, 4);
    public const double ProgressPanelHeight = 44, ProgressBarHeight = 18;

    /// <summary>A TabControl's header strip offset, a tab's padding around its text, and the page inset.</summary>
    public static readonly Thickness TabStripMargin = new(1.6, 2.2, 0, 0);
    public static readonly Thickness TabHeaderPadding = new(7, 1, 7, 1);
    public static readonly Thickness TabPagePadding = new(2.2, 0.6, 2.2, 3);

    /// <summary>AiFgfLog: the find bar Padding(6, 4, 6, 0); its label Margin(0, 8, 6, 0), box Margin(0, 4, 6, 0) 260 wide, buttons Margin(3).</summary>
    public static readonly Thickness FindBarMargin = new(6, 4, 6, 0);
    public static readonly Thickness FindLabelMargin = new(0, 8, 6, 0);
    public static readonly Thickness FindBoxMargin = new(0, 4, 6, 0);
    public static readonly Thickness FindInfoMargin = new(8, 8, 0, 0);

    /// <summary>Win32 draws a FixedSingle edit's 1-px frame in SystemColors.WindowFrame, in every app theme and whether focused or not.</summary>
    public static readonly Color EditFrame = Color.FromRgb(0x64, 0x64, 0x64);

    /// <summary>The NumericUpDown frame, drawn by the visual style.</summary>
    public static readonly Color SpinnerFrame = Color.FromRgb(0xAB, 0xAD, 0xB3);

    /// <summary>
    /// GDI lays a monospaced font out on whole device pixels: the em is rounded to a pixel and each
    /// advance hinted to one (Consolas 9.5 pt at 125%: 9 px, where Skia places 8.7 — a log line came
    /// out 3% narrower than in WinForms). The letter spacing that gives Skia the same pitch at
    /// <paramref name="scale"/>; 0 if the font is not found.
    /// </summary>
    public static double GdiMonoSpacing(FontFamily family, double fontSize, double scale)
    {
        if (scale <= 0 || !FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out IGlyphTypeface? face)) return 0;
        double ratio = face.GetGlyphAdvance(face.GetGlyph('0')) / (double)face.Metrics.DesignEmHeight;
        double gdi = Math.Round(ratio * Math.Round(fontSize * scale)), skia = ratio * fontSize * scale;
        return (gdi - skia) / scale;
    }

    /// <summary>The StatusStrip text: 9 pt (the system status font), at 10.4, 2.4 in a 20-px strip.</summary>
    public const double StatusFontSize = 9 * 96 / 72.0;
    public static readonly Thickness StatusMargin = new(10.4, 2.4, 0, 1.6);
}
