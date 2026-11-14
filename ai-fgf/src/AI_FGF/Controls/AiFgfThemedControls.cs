using System;
using System.Drawing;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// A progress bar in the theme's colours: a Border-coloured track and an Accent fill. The system
/// bar is always light grey and green — a bright stripe in a dark window.
/// </summary>
public sealed class AiFgfProgressBar : ProgressBar
{
    public AiFgfProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

#if NET11_0_OR_GREATER
    /// <summary>
    /// .NET 11: how the bar is drawn. Raised and Glow draw it with Direct3D 12 (<see cref="AiFgfDirect3D"/>): a sunken
    /// track (the pressed face, shaded at the top like a notch) and a raised fill lit from above, or a glowing fill in
    /// the accent. Flat, or without a working Direct3D 12, it is the flat bar. ToolForm sets it with the buttons' style.
    /// </summary>
    [System.ComponentModel.DefaultValue(AiFgfButtonStyle.Flat)]
    public AiFgfButtonStyle Look
    {
        get => _look;
        set { if (_look != value) { _look = value; Invalidate(); } }
    }

    private AiFgfButtonStyle _look;

    /// <summary>True when the last paint used Direct3D faces (diagnostics, tests).</summary>
    public bool PaintedDirect3D { get; private set; }

    /// <summary>The width of the fill in device pixels for the current value.</summary>
    public int FillWidth(int width) => (int)Math.Round((double)(Math.Clamp(Value, Minimum, Maximum) - Minimum) / Math.Max(1, Maximum - Minimum) * width);

    private bool PaintDirect3D(PaintEventArgs e)
    {
        AiFgfTheme t = AiFgfTheme.Current;
        float scale = DeviceDpi / 96f;
        Color back = Parent?.BackColor is { A: 255 } pb ? pb : t.App;
        Color trackBody = AiFgfColor.Mix(t.App, t.Border, 0.6);
        Bitmap? track = AiFgfDirect3D.Draw(new AiFgfDirect3D.Face(Width, Height, scale, trackBody, back, t.Border, 0f, 1f));
        if (track is null) return false;
        e.Graphics.DrawImageUnscaled(track, 0, 0);
        int w = FillWidth(Width);
        if (w >= 4)
        {
            bool glow = _look == AiFgfButtonStyle.Glow;
            Bitmap? fill = AiFgfDirect3D.Draw(new AiFgfDirect3D.Face(w, Height, scale, t.Accent, trackBody, Color.Transparent, 0f, 0f,
                glow ? AiFgfColor.Mix(t.Accent, Color.White, 0.25) : default));
            if (fill is null) return false;
            e.Graphics.DrawImageUnscaled(fill, 0, 0);
        }
        return true;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Invalidate(); }
#endif

    protected override void OnPaint(PaintEventArgs e)
    {
#if NET11_0_OR_GREATER
        PaintedDirect3D = _look != AiFgfButtonStyle.Flat && PaintDirect3D(e);
        if (PaintedDirect3D) return;
#endif
        AiFgfTheme t = AiFgfTheme.Current;
        Rectangle r = ClientRectangle;
        using (var track = new SolidBrush(AiFgfColor.Mix(t.App, t.Border, 0.6))) e.Graphics.FillRectangle(track, r);
        int range = Math.Max(1, Maximum - Minimum);
        int w = (int)Math.Round((double)(Value - Minimum) / range * r.Width);
        if (w > 0) using (var fill = new SolidBrush(t.Accent)) e.Graphics.FillRectangle(fill, new Rectangle(r.X, r.Y, w, r.Height));
        using var edge = new Pen(t.Border);
        e.Graphics.DrawRectangle(edge, r.X, r.Y, r.Width - 1, r.Height - 1);
    }
}

/// <summary>
/// A tab control in the theme's colours: the strip behind the tabs is the window colour, the
/// selected tab is the page colour with an accent line, the others are muted — instead of the
/// system's white tab headers, which glare in a dark window.
/// </summary>
public sealed class AiFgfTabControl : TabControl
{
    public AiFgfTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        ScalePadding();   // before the handle: setting it later recreates the window
    }

    /// <summary>
    /// The tab headers' padding, scaled with the DPI. The system's 6x3 px stays 6x3 device pixels at any scale,
    /// so at 150 % the headers came out 3 logical px lower and 9 narrower than at 125 % (and than on Avalonia).
    /// </summary>
    private void ScalePadding()
    {
        var padding = new Point(LogicalToDeviceUnits(6), LogicalToDeviceUnits(3));
        if (Padding != padding) Padding = padding;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ScalePadding();
    }

#if NET11_0_OR_GREATER
    /// <summary>
    /// .NET 11: how the tab headers are drawn. Raised and Glow draw them with Direct3D 12 (<see cref="AiFgfDirect3D"/>):
    /// the selected tab a raised face lit from above (glowing in the accent with Glow), the others sunken like a
    /// pressed button. Flat, or without a working Direct3D 12, the flat headers. ToolForm sets it with the buttons' style.
    /// </summary>
    [System.ComponentModel.DefaultValue(AiFgfButtonStyle.Flat)]
    public AiFgfButtonStyle Look
    {
        get => _look;
        set { if (_look != value) { _look = value; Invalidate(); } }
    }

    private AiFgfButtonStyle _look;

    /// <summary>True when the last paint drew the headers with Direct3D (diagnostics, tests).</summary>
    public bool PaintedDirect3D { get; private set; }

    /// <summary>The headers as Direct3D faces with GDI text; false when Direct3D cannot draw one (paint flat instead).</summary>
    private bool PaintHeadersDirect3D(Graphics g, AiFgfTheme t)
    {
        float scale = DeviceDpi / 96f;
        bool glow = _look == AiFgfButtonStyle.Glow;
        var faces = new Bitmap[TabCount];
        for (int i = 0; i < TabCount; i++)
        {
            Rectangle r = GetTabRect(i);
            bool selected = i == SelectedIndex;
            Color body = selected ? (TabPages[i].BackColor.A == 255 ? TabPages[i].BackColor : t.App) : AiFgfColor.Mix(t.App, t.Border, 0.25);
            Bitmap? face = AiFgfDirect3D.Draw(new AiFgfDirect3D.Face(r.Width, r.Height, scale, body, t.App, t.Border, 0f, selected ? 0f : 1f,
                selected && glow ? t.Accent : default, selected && glow && Focused));
            if (face is null) return false;
            faces[i] = face;
        }
        for (int i = 0; i < TabCount; i++)
        {
            Rectangle r = GetTabRect(i);
            bool selected = i == SelectedIndex;
            g.DrawImageUnscaled(faces[i], r.X, r.Y);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            // a sunken face sits a little lower; the raised one keeps its caption above the shadow
            Rectangle text = selected ? new Rectangle(r.X, r.Y, r.Width, r.Height - (int)Math.Round(1.5f * scale)) : new Rectangle(r.X, r.Y + 1, r.Width, r.Height - 1);
            // the caption against the face as drawn: on a shaded sunken face the theme's Subtle rendered at 2.5:1 (the strokes of
            // small anti-aliased text never reach the full colour), so the colour itself keeps 7:1
            Color under = faces[i].GetPixel(faces[i].Width / 2, Math.Clamp(text.Y - r.Y + text.Height / 2, 0, faces[i].Height - 1));
            Color caption = AiFgfColor.EnsureContrast(selected ? t.Text : t.Subtle, 7.0, under);
            TextRenderer.DrawText(g, TabPages[i].Text, Font, text, caption, flags);
        }
        return true;
    }

    // a glowing selected tab is brighter while the strip has the focus
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (_look == AiFgfButtonStyle.Glow) Invalidate(); }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (_look == AiFgfButtonStyle.Glow) Invalidate(); }
#endif

    protected override void OnPaint(PaintEventArgs e)
    {
        AiFgfTheme t = AiFgfTheme.Current;
        Graphics g = e.Graphics;
        using (var back = new SolidBrush(t.App)) g.FillRectangle(back, ClientRectangle);

        // the page frame
        Rectangle page = DisplayRectangle;
        page.Inflate(2, 2);
        using (var border = new Pen(t.Border)) g.DrawRectangle(border, page.X, page.Y, page.Width - 1, page.Height - 1);

#if NET11_0_OR_GREATER
        PaintedDirect3D = _look != AiFgfButtonStyle.Flat && PaintHeadersDirect3D(g, t);
        if (PaintedDirect3D) return;
#endif
        for (int i = 0; i < TabCount; i++)
        {
            Rectangle r = GetTabRect(i);
            bool selected = i == SelectedIndex;
            Color face = selected ? (TabPages[i].BackColor.A == 255 ? TabPages[i].BackColor : t.App) : AiFgfColor.Mix(t.App, t.Border, 0.25);
            using (var fill = new SolidBrush(face)) g.FillRectangle(fill, r);
            using (var border = new Pen(t.Border)) g.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - (selected ? 0 : 1));
            if (selected)
            {
                int line = Math.Max(2, LogicalToDeviceUnits(2));
                using var accent = new SolidBrush(t.Accent);
                g.FillRectangle(accent, new Rectangle(r.X, r.Y, r.Width, line));
            }
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            TextRenderer.DrawText(g, TabPages[i].Text, Font, r, selected ? t.Text : t.Subtle, flags);
        }
    }
}
