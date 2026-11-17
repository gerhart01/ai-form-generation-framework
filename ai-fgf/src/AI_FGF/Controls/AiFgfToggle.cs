#if NET11_0_OR_GREATER
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// .NET 11: an on/off switch — a check box drawn as a pill-shaped track with a round knob. With <see cref="Look"/> Raised
/// or Glow both are Direct3D 12 faces (<see cref="AiFgfDirect3D"/>): the track sunken while off and raised in the accent
/// while on (glowing with Glow), the knob a raised disc that slides across; flat when Direct3D is unavailable, with Flat,
/// or disabled. It is a <see cref="CheckBox"/> underneath, so values ("true"/"false"), Space, mnemonics and accessibility
/// work as for a check box; the caption, right of the switch, is drawn by the control in the theme's colours.
/// </summary>
public sealed class AiFgfToggle : CheckBox
{
    private AiFgfButtonStyle _look;
    private float _knob;            // 0 = off (left) .. 1 = on (right), eased
    private bool _hover;
    private System.Windows.Forms.Timer? _anim;

    public AiFgfToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        AutoSize = true;
        BackColor = Color.Transparent;
    }

    /// <summary>How the switch is drawn: flat, or raised / glowing Direct3D faces. ToolForm sets it with the buttons' style.</summary>
    [DefaultValue(AiFgfButtonStyle.Flat)]
    public AiFgfButtonStyle Look
    {
        get => _look;
        set { if (_look != value) { _look = value; Invalidate(); } }
    }

    /// <summary>True when the last paint used Direct3D faces (diagnostics, tests).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool PaintedDirect3D { get; private set; }

    /// <summary>Where the knob is now: 0 = off, 1 = on (it eases between them).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float KnobPosition => _knob;

    /// <summary>The switch's track (client coordinates).</summary>
    public Rectangle TrackBounds
    {
        get
        {
            int h = TrackHeight, w = (int)Math.Round(h * 1.85);
            // right to left: the switch at the right, the caption to its left (as a check box's box and caption)
            int x = Rtl ? ClientSize.Width - Padding.Right - w : Padding.Left;
            return new Rectangle(x, Math.Max(0, (ClientSize.Height - h) / 2), w, h);
        }
    }

    /// <summary>True when the control is laid out right to left (its own setting, or inherited).</summary>
    private bool Rtl
    {
        get
        {
            for (Control? p = this; p is not null; p = p.Parent)
                if (p.RightToLeft != RightToLeft.Inherit) return p.RightToLeft == RightToLeft.Yes;
            return false;
        }
    }

    private int TrackHeight => Math.Max(LogicalToDeviceUnits(14), Font.Height + LogicalToDeviceUnits(2));

    private int Gap => LogicalToDeviceUnits(7);

    /// <summary>The caption wraps past this many em, and never runs wider than the wrapping row it sits in (as <see cref="AiFgfCheck"/>).</summary>
    [DefaultValue(25)]
    public int MaxTextEm { get; set; } = 25;

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        int h = TrackHeight, w = (int)Math.Round(h * 1.85);
        var natural = new Size(Padding.Horizontal + w + Gap + text.Width + LogicalToDeviceUnits(2),
                               Padding.Vertical + Math.Max(h, text.Height) + LogicalToDeviceUnits(2));
        // a long caption wraps (it was one line, cut off with an ellipsis or sticking out of a narrow row)
        return AiFgfWrap.Preferred(this, natural, MaxTextEm, Padding.Horizontal + w + Gap + LogicalToDeviceUnits(2), Padding.Vertical + LogicalToDeviceUnits(2));
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        if (!IsHandleCreated || !Visible) { _knob = Checked ? 1f : 0f; Invalidate(); return; }
        _anim ??= CreateTimer();
        _anim.Start();
    }

    private System.Windows.Forms.Timer CreateTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 15 };
        t.Tick += (_, _) =>
        {
            float target = Checked ? 1f : 0f;
            _knob = _knob < target ? Math.Min(target, _knob + 0.2f) : Math.Max(target, _knob - 0.2f);
            Invalidate();
            if (_knob == target) t.Stop();
        };
        return t;
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); _knob = Checked ? 1f : 0f; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); Invalidate(); }

    protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); Invalidate(); }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        AiFgfTheme t = AiFgfTheme.Current;
        Graphics g = e.Graphics;
        Color back = BackGround();
        using (var fill = new SolidBrush(back)) g.FillRectangle(fill, ClientRectangle);

        Rectangle track = TrackBounds;
        PaintedDirect3D = Enabled && _look != AiFgfButtonStyle.Flat && PaintDirect3D(g, track, back, t);
        if (!PaintedDirect3D) PaintFlat(g, track, t);

        bool rtl = Rtl;
        var text = rtl ? new Rectangle(Padding.Left, 0, Math.Max(0, track.Left - Gap - Padding.Left), ClientSize.Height)
                       : new Rectangle(track.Right + Gap, 0, Math.Max(0, ClientSize.Width - track.Right - Gap - Padding.Right), ClientSize.Height);
        // one line when it fits, else wrapped at words (GetPreferredSize made the control tall enough)
        Size size = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, text.Width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis
                    | (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? (ForeColor.A == 255 ? ForeColor : t.Text) : t.Subtle, flags);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, new Rectangle((rtl ? text.Right - Math.Min(text.Width, size.Width) : text.X) - 1, (ClientSize.Height - size.Height) / 2 - 1, Math.Min(text.Width, size.Width) + 2, Math.Min(ClientSize.Height, size.Height + 2)));
    }

    /// <summary>The colour behind the control: its parent's, or the window's when that is transparent too.</summary>
    private Color BackGround()
    {
        for (Control? p = Parent; p is not null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return AiFgfTheme.Current.App;
    }

    private Color TrackColour(AiFgfTheme t) => Checked ? t.Accent : AiFgfColor.Mix(t.App, t.Border, 0.6);

    // the knob stands out from its track in every theme: the accent's text colour on the accent (white, or dark on a
    // light accent), the subtle colour on the grey track - the field colour was lost on the accent in the dark theme
    private Color KnobColour(AiFgfTheme t) => Checked ? t.OnAccent : t.Subtle;

    private bool PaintDirect3D(Graphics g, Rectangle track, Color back, AiFgfTheme t)
    {
        float scale = DeviceDpi / 96f;
        bool glow = _look == AiFgfButtonStyle.Glow && Checked;
        Color trackBody = TrackColour(t);
        Bitmap? trackFace = AiFgfDirect3D.Draw(new AiFgfFace(track.Width, track.Height, scale, trackBody, back, t.Border, _hover ? 1f : 0f,
            Checked ? 0f : 1f, glow ? AiFgfColor.Mix(t.Accent, Color.White, 0.25) : default, glow && Focused, Radius: track.Height / 2f));
        if (trackFace is null) return false;
        Rectangle knob = KnobBounds(track);
        Bitmap? knobFace = AiFgfDirect3D.Draw(new AiFgfFace(knob.Width, knob.Height, scale, KnobColour(t), trackBody, Color.Transparent, _hover ? 1f : 0f, 0f,
            Radius: knob.Height / 2f));
        if (knobFace is null) return false;
        g.DrawImageUnscaled(trackFace, track.X, track.Y);
        g.DrawImageUnscaled(knobFace, knob.X, knob.Y);
        return true;
    }

    private Rectangle KnobBounds(Rectangle track)
    {
        int inset = Math.Max(2, LogicalToDeviceUnits(2)), size = Math.Max(4, track.Height - 2 * inset);
        float along = Rtl ? 1f - _knob : _knob;   // mirrored right to left: on is the left end
        int x = track.X + inset + (int)Math.Round((track.Width - 2 * inset - size) * along);
        return new Rectangle(x, track.Y + inset, size, size);
    }

    private void PaintFlat(Graphics g, Rectangle track, AiFgfTheme t)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color body = Enabled ? TrackColour(t) : t.App;
        using (GraphicsPath pill = Pill(track))
        {
            using (var fill = new SolidBrush(body)) g.FillPath(fill, pill);
            using var edge = new Pen(Enabled && Checked ? t.Accent : t.Border);
            g.DrawPath(edge, pill);
        }
        Rectangle knob = KnobBounds(track);
        using (var fill = new SolidBrush(Enabled ? KnobColour(t) : AiFgfColor.Mix(t.App, t.Border, 0.4))) g.FillEllipse(fill, knob);
        using (var edge = new Pen(t.Border)) g.DrawEllipse(edge, knob);
        g.SmoothingMode = SmoothingMode.Default;
    }

    private static GraphicsPath Pill(Rectangle r)
    {
        var path = new GraphicsPath();
        int d = r.Height;
        path.AddArc(r.X, r.Y, d - 1, d - 1, 90, 180);
        path.AddArc(r.Right - d, r.Y, d - 1, d - 1, 270, 180);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _anim?.Dispose();
        base.Dispose(disposing);
    }
}
#endif
