#if NET11_0_OR_GREATER
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// .NET 11: the Direct3D glyph of a check box or radio button (<see cref="AiFgfCheck"/>, <see cref="AiFgfRadio"/>) - one
/// painter for both: a sunken well (round or rounded square) over the system's glyph, and when on a raised accent face -
/// a dot, or a square with a tick (a dash when indeterminate). Returns false when Direct3D is unavailable (the caller
/// leaves the system's glyph).
/// </summary>
internal static class AiFgfGlyph3D
{
    /// <summary>The system's glyph square (client coordinates): 13 logical pixels, middle left (CheckAlign MiddleLeft).</summary>
    public static Rectangle Bounds(Control c)
    {
        int s = c.LogicalToDeviceUnits(13);
        return new Rectangle(c.Padding.Left, Math.Max(0, (c.ClientSize.Height - s) / 2), s, s);
    }

    /// <summary>The colour of the on-face: the accent, lighter with Glow.</summary>
    public static Color OnColour(AiFgfButtonStyle look)
        => look == AiFgfButtonStyle.Glow ? AiFgfColor.Mix(AiFgfTheme.Current.Accent, Color.White, 0.2) : AiFgfTheme.Current.Accent;

    /// <summary>
    /// True when the system's glyph sits where <see cref="Bounds"/> says - middle left, left to right, a box (not a push
    /// button). Any other layout keeps the system's glyph: a 3D one drawn at the left would cover the caption.
    /// </summary>
    public static bool Fits(ButtonBase b) => b switch
    {
        CheckBox cb => cb.Appearance == Appearance.Normal && cb.CheckAlign == ContentAlignment.MiddleLeft && Ltr(cb),
        RadioButton rb => rb.Appearance == Appearance.Normal && rb.CheckAlign == ContentAlignment.MiddleLeft && Ltr(rb),
        _ => false,
    };

    private static bool Ltr(Control c)
    {
        for (Control? p = c; p is not null; p = p.Parent)
            if (p.RightToLeft != RightToLeft.Inherit) return p.RightToLeft == RightToLeft.No;
        return true;
    }

    public static bool Paint(ButtonBase c, Graphics g, AiFgfButtonStyle look, bool round, CheckState state, bool hover)
    {
        if (!Fits(c)) return false;
        AiFgfTheme t = AiFgfTheme.Current;
        float scale = c.DeviceDpi / 96f;
        Rectangle glyph = Bounds(c);
        // one pixel more each side: the faces' corners take the background colour and hide the system's glyph under them
        Rectangle well = Rectangle.Inflate(glyph, 1, 1);
        float corner = round ? well.Width / 2f : 2.5f * scale;
        Bitmap? wellFace = AiFgfDirect3D.Draw(new AiFgfFace(well.Width, well.Height, scale, t.Surface, Background(c), t.Border,
            hover ? 1f : 0f, 1f, Radius: corner));
        if (wellFace is null) return false;
        Bitmap? onFace = null;
        Rectangle on = Rectangle.Empty;
        if (state != CheckState.Unchecked)
        {
            int d = round ? Math.Max(4, (int)Math.Round(glyph.Width * 0.55)) : Math.Max(6, glyph.Width - 2 * Math.Max(1, (int)Math.Round(scale)));
            on = new Rectangle(glyph.X + (glyph.Width - d) / 2, glyph.Y + (glyph.Height - d) / 2, d, d);
            onFace = AiFgfDirect3D.Draw(new AiFgfFace(d, d, scale, OnColour(look), t.Surface, Color.Transparent, hover ? 1f : 0f, 0f,
                Radius: round ? d / 2f : 2f * scale));
            if (onFace is null) return false;
        }
        g.DrawImageUnscaled(wellFace, well.X, well.Y);
        if (onFace is null) return true;
        g.DrawImageUnscaled(onFace, on.X, on.Y);
        if (!round) Mark(g, on, state, scale, t.OnAccent);
        return true;
    }

    // the tick (or the indeterminate dash) in the accent's text colour, drawn over the raised square
    private static void Mark(Graphics g, Rectangle r, CheckState state, float scale, Color colour)
    {
        SmoothingMode old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(colour, Math.Max(1.5f, 1.6f * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        if (state == CheckState.Indeterminate) g.DrawLine(pen, x + w * 0.25f, y + h * 0.5f, x + w * 0.75f, y + h * 0.5f);
        else g.DrawLines(pen, new[] { new PointF(x + w * 0.22f, y + h * 0.52f), new PointF(x + w * 0.42f, y + h * 0.72f), new PointF(x + w * 0.78f, y + h * 0.3f) });
        g.SmoothingMode = old;
    }

    private static Color Background(Control c)
    {
        if (c.BackColor.A == 255) return c.BackColor;
        for (Control? p = c.Parent; p is not null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return AiFgfTheme.Current.App;
    }
}
#endif
