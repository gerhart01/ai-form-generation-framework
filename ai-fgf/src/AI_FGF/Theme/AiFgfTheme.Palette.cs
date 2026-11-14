using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>WinForms side of <see cref="AiFgfThemeChoice"/>.</summary>
public static class AiFgfThemeChoiceExtensions
{
    /// <summary>The WinForms theme for this choice (<see cref="AiFgfTheme.Create"/>).</summary>
    public static AiFgfTheme ToTheme(this AiFgfThemeChoice choice) => AiFgfTheme.Create(choice.Accent, choice.Dark, choice.Tint);
}

public sealed partial class AiFgfTheme
{
    /// <summary>The choice this theme was built from by <see cref="Create"/>; null for a hand-made theme.</summary>
    public AiFgfThemeChoice? Choice { get; init; }

    /// <summary>Ready-made accent colours (Windows accent palette).</summary>
    public static IReadOnlyList<(string Name, Color Color)> Accents => AiFgfPalette.Accents;

    /// <summary>Minimum contrast every text/background pair of a created theme meets (WCAG AA).</summary>
    public const double MinContrast = AiFgfPalette.MinContrast;

    /// <summary>
    /// A complete theme from one accent colour. <paramref name="dark"/> picks the dark variant;
    /// <paramref name="tint"/> (0–1) is how strongly the window, field and grid backgrounds take
    /// on the accent's hue. Every text/background pair (see <see cref="TextPairs"/>) is adjusted to
    /// at least 4.5:1 — a pale or neon accent is darkened just enough to carry white button text,
    /// so any choice stays readable.
    /// </summary>
    public static AiFgfTheme Create(Color accent, bool dark = false, double tint = 0.15)
    {
        AiFgfPalette p = AiFgfPalette.Create(accent, dark, tint);   // the colour maths is shared with the Avalonia front end
        return new AiFgfTheme
        {
            App = p.App, Surface = p.Surface, Text = p.Text, Subtle = p.Subtle, Border = p.Border,
            Accent = p.Accent, AccentHover = p.AccentHover, OnAccent = p.OnAccent,
            Invalid = p.Invalid, HoverFill = p.HoverFill, PressFill = p.PressFill,
            GridHeader = p.GridHeader, GridAlt = p.GridAlt, GridSelection = p.GridSelection, GridLines = p.GridLines,
            LogBackground = p.LogBackground, LogForeground = p.LogForeground,
            LogAccent = p.LogAccent, LogError = p.LogError, LogSuccess = p.LogSuccess, LogWarn = p.LogWarn,
            Choice = p.Choice,
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

    // ------------------------------------------------------------ runtime switch

    /// <summary>Raised after <see cref="Switch(AiFgfTheme)"/> changed the theme.</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Make <paramref name="theme"/> current and recolour every open window in place: each colour
    /// that plays a role in the old theme (window, field, accent, grid, border, text…) gets the
    /// same role's colour in the new one; colours an app set itself are left alone. The title bar
    /// follows light/dark.
    /// </summary>
    public static void Switch(AiFgfTheme theme) => Switch(theme, null);

    /// <summary>
    /// <see cref="Switch(AiFgfTheme)"/>, also recolouring <paramref name="loading"/> — a form in its
    /// <c>OnLoad</c> is not in <see cref="Application.OpenForms"/> yet.
    /// </summary>
    public static void Switch(AiFgfTheme theme, Form? loading)
    {
        ArgumentNullException.ThrowIfNull(theme);
        AiFgfTheme from = Current;
        if (ReferenceEquals(from, theme)) return;
        Current = theme;
        var forms = Application.OpenForms.Cast<Form>().ToList();
        if (loading is not null && !forms.Contains(loading)) forms.Add(loading);
        foreach (Form f in forms)
        {
            Recolor(f, from, theme);
            Dwm.Refresh(f);
            AiFgfNativeTheme.Refresh(f);
            f.Invalidate(true);
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static readonly Func<AiFgfTheme, Color>[] BackRoles =
    {
        t => t.App, t => t.Surface, t => t.Accent, t => t.AccentHover, t => t.HoverFill, t => t.PressFill, t => t.Invalid,
        t => t.GridHeader, t => t.GridAlt, t => t.GridSelection, t => t.GridLines, t => t.Border, t => t.LogBackground,
    };
    private static readonly Func<AiFgfTheme, Color>[] ForeRoles =
    {
        t => t.Text, t => t.Subtle, t => t.OnAccent, t => t.LogForeground, t => t.LogAccent, t => t.LogError, t => t.LogSuccess, t => t.LogWarn,
    };
    private static readonly Func<AiFgfTheme, Color>[] LogBackRoles = { t => t.LogBackground };
    private static readonly Func<AiFgfTheme, Color>[] LogForeRoles = { t => t.LogForeground };

    /// <summary>
    /// The colour playing <paramref name="c"/>'s role in <paramref name="from"/>, taken from
    /// <paramref name="to"/>; <paramref name="c"/> itself if it plays no role.
    /// </summary>
    public static Color Remap(Color c, AiFgfTheme from, AiFgfTheme to, bool foreground)
        => Remap(c, from, to, foreground ? ForeRoles : BackRoles);

    private static Color Remap(Color c, AiFgfTheme from, AiFgfTheme to, Func<AiFgfTheme, Color>[] roles)
    {
        if (c.IsEmpty || c.A < 255) return c;
        int argb = c.ToArgb();
        foreach (var role in roles) if (role(from).ToArgb() == argb) return role(to);
        return c;
    }

    /// <summary>
    /// Put this in a control's <see cref="Control.Tag"/> to keep its own colours on a theme switch
    /// (a colour swatch that happens to equal the old accent must not become the new accent).
    /// </summary>
    public static readonly object KeepColors = new();

    /// <summary>Recolour <paramref name="root"/> and its children from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static void Recolor(Control root, AiFgfTheme from, AiFgfTheme to)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (ReferenceEquals(root.Tag, KeepColors))
        {
            foreach (Control child in root.Controls) Recolor(child, from, to);
            return;
        }
        bool log = root is RichTextBox;
        var back = log ? LogBackRoles : BackRoles;
        var fore = log ? LogForeRoles : ForeRoles;

        // only colours set on this control — an inherited one follows its parent by itself
        var props = TypeDescriptor.GetProperties(root);
        if (props["BackColor"]?.ShouldSerializeValue(root) == true) root.BackColor = Remap(root.BackColor, from, to, back);
        if (props["ForeColor"]?.ShouldSerializeValue(root) == true) root.ForeColor = Remap(root.ForeColor, from, to, fore);

        switch (root)
        {
            case ButtonBase { FlatStyle: FlatStyle.Flat } b:
                b.FlatAppearance.BorderColor = Remap(b.FlatAppearance.BorderColor, from, to, BackRoles);
                b.FlatAppearance.MouseOverBackColor = Remap(b.FlatAppearance.MouseOverBackColor, from, to, BackRoles);
                b.FlatAppearance.MouseDownBackColor = Remap(b.FlatAppearance.MouseDownBackColor, from, to, BackRoles);
                break;
            case DataGridView g:
                g.BackgroundColor = Remap(g.BackgroundColor, from, to, BackRoles);
                g.GridColor = Remap(g.GridColor, from, to, BackRoles);
                foreach (DataGridViewCellStyle st in new[] { g.DefaultCellStyle, g.AlternatingRowsDefaultCellStyle, g.ColumnHeadersDefaultCellStyle })
                {
                    st.BackColor = Remap(st.BackColor, from, to, BackRoles);
                    st.ForeColor = Remap(st.ForeColor, from, to, ForeRoles);
                    st.SelectionBackColor = Remap(st.SelectionBackColor, from, to, BackRoles);
                    st.SelectionForeColor = Remap(st.SelectionForeColor, from, to, ForeRoles);
                }
                break;
            case ToolStrip strip:
                foreach (ToolStripItem item in strip.Items)
                {
                    item.ForeColor = Remap(item.ForeColor, from, to, ForeRoles);
                    item.BackColor = Remap(item.BackColor, from, to, BackRoles);
                }
                break;
        }
        foreach (Control child in root.Controls) Recolor(child, from, to);
    }
}
