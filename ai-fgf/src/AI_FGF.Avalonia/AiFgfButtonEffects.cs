using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using DColor = System.Drawing.Color;

namespace AI_FGF.Avalonia;

/// <summary>
/// The button styles of <see cref="AiFgfEffects"/> on Avalonia — what WinForms draws with Direct3D 12 is here a
/// drop shadow: Raised — a soft dark shadow below, deeper on hover, almost none when pressed; Glow — no offset,
/// in the accent colour (lightened on accent buttons): idle, stronger on focus, strongest on hover, tighter when
/// pressed. Disabled buttons have none; spinner arrows and check boxes are not buttons here. The window's styles
/// follow theme switches and <see cref="AiFgfEffects.Changed"/>.
/// </summary>
internal static class AiFgfButtonEffects
{
    public static void Attach(Window window, Func<AiFgfButtonStyle> style)
    {
        Styles current = For(style(), AiFgfTheme.Current);
        window.Styles.Add(current);
        void Refresh(object? sender, EventArgs e)
        {
            int i = window.Styles.IndexOf(current);
            current = For(style(), AiFgfTheme.Current);
            if (i >= 0) window.Styles[i] = current; else window.Styles.Add(current);
        }
        AiFgfTheme.Changed += Refresh;
        AiFgfEffects.Changed += Refresh;
        window.Closed += (_, _) => { AiFgfTheme.Changed -= Refresh; AiFgfEffects.Changed -= Refresh; };
    }

    public static Styles For(AiFgfButtonStyle style, AiFgfTheme t)
    {
        var styles = new Styles();
        if (style == AiFgfButtonStyle.Flat) return styles;
        foreach (bool accent in new[] { false, true })
        {
            Selector Of(Selector? x) { Selector s = x.OfType<Button>(); return accent ? s.Class("accent") : s; }
            if (style == AiFgfButtonStyle.Glow)
            {
                Color c = AvTheme.ToAv(accent ? AiFgfColor.Mix(t.Accent, DColor.White, 0.35) : t.Accent);
                styles.Add(Shadow(x => Of(x), c, 0, 8, 0.5));
                styles.Add(Shadow(x => Of(x).Class(":focus-within"), c, 0, 10, 0.75));
                styles.Add(Shadow(x => Of(x).Class(":pointerover"), c, 0, 12, 0.95));
                styles.Add(Shadow(x => Of(x).Class(":pressed"), c, 0, 6, 0.8));
            }
            else
            {
                Color c = Colors.Black;
                styles.Add(Shadow(x => Of(x), c, 1.5, 4, t.IsDark ? 0.6 : 0.3));
                styles.Add(Shadow(x => Of(x).Class(":pointerover"), c, 2, 6, t.IsDark ? 0.7 : 0.4));
                styles.Add(Shadow(x => Of(x).Class(":pressed"), c, 0.5, 1.5, t.IsDark ? 0.4 : 0.2));
            }
        }
        if (style == AiFgfButtonStyle.Glow)
        {
            // the same glow, now on text/combo/number fields when they have focus - mirrors AiFgfFieldGlow on WinForms
            Color fc = AvTheme.ToAv(t.Accent);
            styles.Add(Shadow(x => x.OfType<TextBox>().Class(":focus-within"), fc, 0, 10, 0.6));
            styles.Add(Shadow(x => x.OfType<ComboBox>().Class(":focus-within"), fc, 0, 10, 0.6));
            styles.Add(Shadow(x => x.OfType<NumericUpDown>().Class(":focus-within"), fc, 0, 10, 0.6));
        }

        styles.Add(new Style(x => x.OfType<Button>().Class(":disabled")) { Setters = { new Setter(Visual.EffectProperty, null) } });
        return styles;
    }

    private static Style Shadow(Func<Selector?, Selector> selector, Color color, double offsetY, double blur, double opacity) => new(selector)
    {
        Setters = { new Setter(Visual.EffectProperty, new DropShadowEffect { OffsetX = 0, OffsetY = offsetY, BlurRadius = blur, Color = color, Opacity = opacity }) },
    };
}
