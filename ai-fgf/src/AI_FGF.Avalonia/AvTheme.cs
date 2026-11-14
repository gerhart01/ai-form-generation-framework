using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using DColor = System.Drawing.Color;

namespace AI_FGF.Avalonia;

/// <summary>
/// Puts an <see cref="AiFgfPalette"/> into the Avalonia application: light/dark variant, Fluent's
/// accent family, the Fluent resources that give controls the WinForms look (Segoe UI 9.25 pt,
/// square flat bordered controls, accent primary button, 24-px fields, small tabs), and
/// <c>AiFgf.*</c> brushes the framework's own surfaces bind to — so a theme switch recolours open
/// windows live. Use <see cref="AiFgfTheme"/> from app code.
/// </summary>
public static class AvTheme
{
    /// <summary>Font of the WinForms front end (Segoe UI 9.25 pt); Inter where Segoe UI is missing (Linux).</summary>
    public static readonly FontFamily UiFont = new("Segoe UI, Inter, DejaVu Sans, sans-serif");

    /// <summary>Monospace font of the log.</summary>
    public static readonly FontFamily MonoFont = new("Consolas, DejaVu Sans Mono, Liberation Mono, Cascadia Mono, monospace");   // WinForms: Consolas

    /// <summary>9.25 pt in device-independent pixels — the WinForms UI size.</summary>
    public const double UiFontSize = 9.25 * 96 / 72;

    // keys of the framework's own brushes (bound with Bind below)
    public const string AppKey = "AiFgf.App", SurfaceKey = "AiFgf.Surface", TextKey = "AiFgf.Text", SubtleKey = "AiFgf.Subtle",
        BorderKey = "AiFgf.Border", AccentKey = "AiFgf.Accent", HeaderKey = "AiFgf.GridHeader", TrackKey = "AiFgf.Track",
        LogBackKey = "AiFgf.LogBackground", LogForeKey = "AiFgf.LogForeground";

    public static Color ToAv(DColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static IBrush Brush(DColor c) => new SolidColorBrush(ToAv(c));

    /// <summary>Bind <paramref name="property"/> of <paramref name="control"/> to a theme brush (follows theme switches).</summary>
    public static T Bind<T>(T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    /// <summary>Apply <paramref name="p"/> to <paramref name="app"/>.</summary>
    public static void Apply(Application app, AiFgfPalette p)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.RequestedThemeVariant = p.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var r = app.Resources;
        r["SystemAccentColor"] = ToAv(p.Accent);
        r["SystemAccentColorDark1"] = ToAv(p.AccentHover);
        r["SystemAccentColorDark2"] = ToAv(AiFgfColor.Mix(p.AccentHover, DColor.Black, 0.15));
        r["SystemAccentColorDark3"] = ToAv(AiFgfColor.Mix(p.AccentHover, DColor.Black, 0.3));
        r["SystemAccentColorLight1"] = ToAv(AiFgfColor.Mix(p.Accent, DColor.White, 0.15));
        r["SystemAccentColorLight2"] = ToAv(AiFgfColor.Mix(p.Accent, DColor.White, 0.3));
        r["SystemAccentColorLight3"] = ToAv(AiFgfColor.Mix(p.Accent, DColor.White, 0.45));

        r[AppKey] = Brush(p.App);
        r[SurfaceKey] = Brush(p.Surface);
        r[TextKey] = Brush(p.Text);
        r[SubtleKey] = Brush(p.Subtle);
        r[BorderKey] = Brush(p.Border);
        r[AccentKey] = Brush(p.Accent);
        r[HeaderKey] = Brush(p.GridHeader);
        r[TrackKey] = Brush(AiFgfColor.Mix(p.App, p.Border, 0.6));
        r[LogBackKey] = Brush(p.LogBackground);
        r[LogForeKey] = Brush(p.LogForeground);
        ApplyWindowsLook(r, p);
    }

    /// <summary>
    /// The WinForms look on Fluent: Fluent draws every control from named resources, so filling
    /// them from the palette and the WinForms metrics gives the same flat, square, bordered controls,
    /// accent primary button, UI font and sizes — on Linux as on Windows — without new templates.
    /// </summary>
    private static void ApplyWindowsLook(IResourceDictionary r, AiFgfPalette p)
    {
        IBrush B(DColor c) => Brush(c);
        // geometry and type
        r["ControlCornerRadius"] = new CornerRadius(0);
        r["OverlayCornerRadius"] = new CornerRadius(0);
        r["ContentControlThemeFontFamily"] = UiFont;
        r["ControlContentThemeFontSize"] = UiFontSize;
        r["TextControlThemeMinHeight"] = 20.0;
        r["TextControlThemePadding"] = WinFormsMetrics.EditPadding;
        r["TabItemHeaderFontSize"] = UiFontSize;                       // tabs in the window's font, as in WinForms
        r["TabItemHeaderThemeFontWeight"] = global::Avalonia.Media.FontWeight.Normal;
        r["TabItemMinHeight"] = 0.0;
        r["TabItemHeaderMargin"] = WinFormsMetrics.TabHeaderPadding;

        // neutral buttons: Surface face, 1-px Border, hover/press tints, readable when disabled
        foreach (string k in new[] { "ButtonBackground", "RepeatButtonBackground", "ToggleButtonBackground" }) r[k] = B(p.Surface);
        foreach (string k in new[] { "ButtonBackgroundPointerOver", "RepeatButtonBackgroundPointerOver", "ToggleButtonBackgroundPointerOver" }) r[k] = B(p.HoverFill);
        foreach (string k in new[] { "ButtonBackgroundPressed", "RepeatButtonBackgroundPressed", "ToggleButtonBackgroundPressed" }) r[k] = B(p.PressFill);
        foreach (string k in new[] { "ButtonBackgroundDisabled", "RepeatButtonBackgroundDisabled", "ToggleButtonBackgroundDisabled" }) r[k] = B(p.App);
        foreach (string k in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
                                     "RepeatButtonForeground", "RepeatButtonForegroundPointerOver", "RepeatButtonForegroundPressed",
                                     "ToggleButtonForeground", "ToggleButtonForegroundPointerOver", "ToggleButtonForegroundPressed" }) r[k] = B(p.Text);
        foreach (string k in new[] { "ButtonForegroundDisabled", "RepeatButtonForegroundDisabled", "ToggleButtonForegroundDisabled" }) r[k] = B(p.Subtle);
        foreach (string k in new[] { "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled",
                                     "RepeatButtonBorderBrush", "RepeatButtonBorderBrushPointerOver", "RepeatButtonBorderBrushPressed", "RepeatButtonBorderBrushDisabled" }) r[k] = B(p.Border);
        r["ButtonBorderThemeThickness"] = new Thickness(1);

        // the primary (accent) button
        r["AccentButtonBackground"] = B(p.Accent);
        r["AccentButtonBackgroundPointerOver"] = B(p.AccentHover);
        r["AccentButtonBackgroundPressed"] = B(p.AccentHover);
        r["AccentButtonBackgroundDisabled"] = B(p.App);
        r["AccentButtonForeground"] = B(p.OnAccent);
        r["AccentButtonForegroundPointerOver"] = B(p.OnAccent);
        r["AccentButtonForegroundPressed"] = B(p.OnAccent);
        r["AccentButtonForegroundDisabled"] = B(p.Subtle);
        r["AccentButtonBorderBrush"] = B(p.Accent);
        r["AccentButtonBorderBrushPointerOver"] = B(p.AccentHover);
        r["AccentButtonBorderBrushPressed"] = B(p.AccentHover);
        r["AccentButtonBorderBrushDisabled"] = B(p.Border);

        // text boxes, numbers, memos
        foreach (string k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" }) r[k] = B(p.Surface);
        r["TextControlBackgroundDisabled"] = B(p.App);
        foreach (string k in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" }) r[k] = B(p.Text);
        r["TextControlForegroundDisabled"] = B(p.Subtle);
        // a WinForms FixedSingle edit: the system frame colour, unchanged by hover and focus
        foreach (string k in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            r[k] = new SolidColorBrush(WinFormsMetrics.EditFrame);
        r["TextControlBorderBrushDisabled"] = B(p.Border);
        r["TextControlPlaceholderForeground"] = B(p.Subtle);
        r["TextControlPlaceholderForegroundPointerOver"] = B(p.Subtle);
        r["TextControlPlaceholderForegroundFocused"] = B(p.Subtle);
        r["TextControlBorderThemeThickness"] = new Thickness(1);
        r["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        r["TextControlSelectionHighlightColor"] = B(p.GridSelection);

        // drop-down lists
        foreach (string k in new[] { "ComboBoxBackground", "ComboBoxBackgroundFocused", "ComboBoxDropDownBackground" }) r[k] = B(p.Surface);
        r["ComboBoxBackgroundPointerOver"] = B(p.HoverFill);
        r["ComboBoxBackgroundPressed"] = B(p.PressFill);
        r["ComboBoxBackgroundDisabled"] = B(p.App);
        foreach (string k in new[] { "ComboBoxForeground", "ComboBoxForegroundFocused", "ComboBoxDropDownGlyphForeground" }) r[k] = B(p.Text);
        r["ComboBoxForegroundDisabled"] = B(p.Subtle);
        foreach (string k in new[] { "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed", "ComboBoxBorderBrushDisabled", "ComboBoxDropDownBorderBrush" }) r[k] = B(p.Border);
        r["ComboBoxBorderThemeThickness"] = new Thickness(1);
        r["ComboBoxMinHeight"] = 20.0;
        r["ComboBoxPadding"] = new Thickness(4, 1.7, 4, 1.7);

        // tabs: muted headers, the selected one in the text colour with the accent line
        r["TabItemHeaderForegroundUnselected"] = B(p.Subtle);
        r["TabItemHeaderForegroundUnselectedPointerOver"] = B(p.Text);
        r["TabItemHeaderForegroundSelected"] = B(p.Text);
        r["TabItemHeaderForegroundSelectedPointerOver"] = B(p.Text);
        r["TabItemHeaderSelectedPipeFill"] = B(p.Accent);
        // WinForms tabs: boxes, the selected one in the page colour, the others in the header tint
        foreach (string k in new[] { "TabItemHeaderBackgroundUnselected", "TabItemHeaderBackgroundUnselectedPointerOver", "TabItemHeaderBackgroundUnselectedPressed" }) r[k] = B(p.GridHeader);
        foreach (string k in new[] { "TabItemHeaderBackgroundSelected", "TabItemHeaderBackgroundSelectedPointerOver", "TabItemHeaderBackgroundSelectedPressed" }) r[k] = B(p.Surface);

        // results grid
        r["DataGridColumnHeaderBackgroundBrush"] = B(p.GridHeader);
        r["DataGridColumnHeaderForegroundBrush"] = B(p.Text);
        r["DataGridRowSelectedBackgroundBrush"] = B(p.GridSelection);
        r["DataGridRowSelectedUnfocusedBackgroundBrush"] = B(p.GridSelection);
        r["DataGridGridLinesBrush"] = B(p.GridLines);
    }
}
