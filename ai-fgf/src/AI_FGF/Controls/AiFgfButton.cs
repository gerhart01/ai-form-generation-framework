using System;
using System.Drawing;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// The button behind <see cref="AiFgf.Button"/>, <see cref="AiFgf.SmallButton"/> and the primary action.
/// It differs from <see cref="Button"/> only when disabled: WinForms draws a disabled flat button's
/// text at about 1.5:1 against its background — invisible on most screens, more so on HDR or a
/// projector. Disabled, this one keeps a readable caption in the theme's Subtle colour on a flat
/// App-coloured face, so it is clearly inactive yet still legible.
/// <see cref="Look"/> (<see cref="AiFgfButtonStyle"/>) draws the face with Direct3D 12 (<see cref="AiFgfDirect3D"/>):
/// Raised — lit from above, with a bevel and a soft shadow, sunk when pressed; Glow — a soft halo in
/// <see cref="GlowColor"/> around a slightly smaller body, a hot rim, brighter on hover and focus. Without a
/// working Direct3D 12 it is the flat button above — the same size, colours and text either way.
/// ToolForm sets it from <see cref="AiFgfEffects"/>.
/// </summary>
public sealed class AiFgfButton : Button
{
    /// <summary>How the face is drawn: flat, raised or glowing (the effects need Direct3D 12; flat otherwise).</summary>
    [System.ComponentModel.DefaultValue(AiFgfButtonStyle.Flat)]
    public AiFgfButtonStyle Look
    {
        get => _look;
        set { if (_look != value) { _look = value; Invalidate(); UpdateMagic(); } }
    }

    private bool Effects => _look != AiFgfButtonStyle.Flat;
    private bool Glowing => _look == AiFgfButtonStyle.Glow;

    /// <summary>The glow; empty (the default) follows the theme: the accent, lightened on an accent button.</summary>
    public Color GlowColor
    {
        get => _glowColor;
        set { if (_glowColor != value) { _glowColor = value; Invalidate(); } }
    }

    private bool ShouldSerializeGlowColor() => !_glowColor.IsEmpty;
    private void ResetGlowColor() => GlowColor = Color.Empty;

    /// <summary>The colour the face glows in now.</summary>
    public Color EffectiveGlow
    {
        get
        {
            if (!_glowColor.IsEmpty) return _glowColor;
            AiFgfTheme t = AiFgfTheme.Current;
            bool accent = BackColor.ToArgb() == t.Accent.ToArgb() || BackColor.ToArgb() == t.AccentHover.ToArgb();
            return accent ? AiFgfColor.Mix(t.Accent, Color.White, 0.35) : t.Accent;
        }
    }

    private AiFgfButtonStyle _look;
    private bool _hover, _pressed;
    private Color _glowColor = Color.Empty;
    private PointF _pointer = new(-1f, -1f);
    private System.Windows.Forms.Timer? _animTimer;
    private float _hoverAnim, _pressAnim;

#if NET11_0_OR_GREATER
    /// <summary>
    /// .NET 11: the glow is magic light — the halo's hue drifts around the button like mother-of-pearl, a soft band of
    /// light sweeps the face, sparks twinkle in the halo and it breathes. One cycle is <see cref="MagicPhases"/> frames
    /// (3.2 s), each drawn once and then taken from the face cache. It moves only on an enabled, visible Glow button,
    /// and not at all when Windows has its animations turned off (the light stays, still).
    /// </summary>
    public const int MagicPhases = 32;

    private int _magicPhase;
    private System.Windows.Forms.Timer? _magicTimer;

    /// <summary>The frame of the magic light now, 0..<see cref="MagicPhases"/>-1 (tests and screenshots set it).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int MagicPhase
    {
        get => _magicPhase;
        set { int v = ((value % MagicPhases) + MagicPhases) % MagicPhases; if (v != _magicPhase) { _magicPhase = v; Invalidate(); } }
    }

    /// <summary>True while the magic light is moving.</summary>
    public bool MagicAnimating => _magicTimer?.Enabled == true;

    private void UpdateMagic()
    {
        bool run = Glowing && Enabled && Visible && IsHandleCreated && SystemInformation.UIEffectsEnabled;
        if (run && _magicTimer is null)
        {
            _magicTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _magicTimer.Tick += (_, _) =>
            {
                _magicPhase = (_magicPhase + 1) % MagicPhases;
                if (TopLevelControl is Form { Visible: true, WindowState: not FormWindowState.Minimized }) Invalidate();
            };
        }
        if (_magicTimer is null) return;
        if (run && !_magicTimer.Enabled) _magicTimer.Start();
        else if (!run && _magicTimer.Enabled) _magicTimer.Stop();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateMagic(); }

    protected override void OnHandleDestroyed(EventArgs e) { _magicTimer?.Stop(); base.OnHandleDestroyed(e); }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateMagic(); }

    private float MagicNow => Glowing ? _magicPhase / (float)MagicPhases : -1f;
#else
    private void UpdateMagic() { }

    private static float MagicNow => -1f;
#endif

    /// <summary>True when the last paint used a Direct3D face (diagnostics, tests).</summary>
    public bool PaintedDirect3D { get; private set; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); Animate(); }

    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; _pointer = NoPointer; base.OnMouseLeave(e); Animate(); }

    protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) _pressed = true; base.OnMouseDown(mevent); Animate(); }

    protected override void OnMouseUp(MouseEventArgs mevent) { _pressed = false; base.OnMouseUp(mevent); Animate(); }

    /// <summary>
    /// Raised only: a soft specular highlight follows the pointer, quantized to a 5x5 grid so hovering
    /// keeps landing on the same few cached faces instead of asking the GPU to redraw on every pixel
    /// the mouse crosses. Gives the bevel a tangible, light-moves-as-you-move-your-hand feel.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Effects) return;
        PointF q = Quantize(e.Location);
        if (q == _pointer) return;
        _pointer = q;
        Invalidate();
    }

    private static readonly PointF NoPointer = new(-1f, -1f);

    private PointF Quantize(Point p)
    {
        if (Width <= 1 || Height <= 1) return NoPointer;
        const int Cells = 5;
        int gx = Math.Clamp(p.X * Cells / Width, 0, Cells - 1);
        int gy = Math.Clamp(p.Y * Cells / Height, 0, Cells - 1);
        return new PointF((gx + 0.5f) * Width / Cells, (gy + 0.5f) * Height / Cells);
    }

    /// <summary>
    /// Eases _hoverAnim/_pressAnim toward the hover/pressed target over a few frames (Direct3D
    /// styles only) instead of snapping, so the light and shadow visibly slide rather than jump.
    /// A flat button has no face to animate and just lets the base class redraw itself.
    /// </summary>
    private void Animate()
    {
        if (!Effects) { _hoverAnim = _hover ? 1f : 0f; _pressAnim = _pressed ? 1f : 0f; return; }
        if (_animTimer is null) { _animTimer = new System.Windows.Forms.Timer { Interval = 15 }; _animTimer.Tick += (_, _) => StepAnimation(); }
        if (!_animTimer.Enabled) _animTimer.Start();
        Invalidate();
    }

    private void StepAnimation()
    {
        const float Step = 0.3f;
        float hoverTarget = _hover ? 1f : 0f, pressTarget = _pressed ? 1f : 0f;
        _hoverAnim = MoveToward(_hoverAnim, hoverTarget, Step);
        _pressAnim = MoveToward(_pressAnim, pressTarget, Step);
        Invalidate();
        if (_hoverAnim == hoverTarget && _pressAnim == pressTarget) _animTimer!.Stop();
    }

    private static float MoveToward(float value, float target, float step)
        => value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);

    // Effects, not just Glowing: a Raised button's own PaintDirect3D also draws the focus
    // rectangle (Focused && ShowFocusCues), so Tab must repaint it too, not only Glow's halo.
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (Effects) Invalidate(); }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (Effects) Invalidate(); }

    /// <summary>Show the hover / pressed look without a mouse (tests, screenshots).</summary>
    public void SetVisualState(bool hover, bool pressed) { _hover = hover; _pressed = pressed; _hoverAnim = hover ? 1f : 0f; _pressAnim = pressed ? 1f : 0f; Invalidate(); }

    /// <summary>The Direct3D face with GDI text on it; false when Direct3D cannot draw (paint flat instead).</summary>
    private bool PaintDirect3D(PaintEventArgs e)
    {
        float scale = DeviceDpi / 96f;
        Color back = Parent?.BackColor ?? AiFgfTheme.Current.App;
        if (back.A < 255) back = AiFgfTheme.Current.App;
        Color border = FlatAppearance.BorderSize > 0 && FlatAppearance.BorderColor != Color.Empty ? FlatAppearance.BorderColor : Color.Transparent;
        // the flat button's state colours (theme hover / press fills), so both styles say the same thing
        Color hover = FlatAppearance.MouseOverBackColor, press = FlatAppearance.MouseDownBackColor;
        Color body = _pressed && !press.IsEmpty ? press : (_hover || _pressed) && !hover.IsEmpty ? hover : BackColor;
        Color glow = Glowing ? EffectiveGlow : Color.Empty;
        if (Glowing && glow.A < 255) glow = Color.FromArgb(255, glow);
        bool specular = Effects && _pointer.X >= 0;
        Bitmap? face = AiFgfDirect3D.Draw(new AiFgfFace(Width, Height, scale, body, back, border, _hoverAnim, _pressAnim, glow, Glowing && Focused,
            specular ? _pointer.X : -1f, specular ? _pointer.Y : -1f, MagicNow));
        if (face is null) return false;
        e.Graphics.DrawImageUnscaled(face, 0, 0);

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        int lift = Glowing ? 0 : (int)Math.Round(1.5f * scale), inset = (int)((Glowing ? 5.5f : 3) * scale);
        Rectangle text = new(Padding.Left / 2, (_pressed ? lift : 0), Width - Padding.Left, Height - lift);   // the body's area, as drawn
        TextRenderer.DrawText(e.Graphics, Text, Font, text, ForeColor, flags);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(text, -inset, -inset), ForeColor, BackColor);
        return true;
    }
    /// <summary>
    /// Captions wider than this many em wrap onto more lines: the button grows in height instead
    /// of past the screen (a 90-character caption at 20 pt was 1160 px wide).
    /// </summary>
    [System.ComponentModel.DefaultValue(25)]
    public int MaxTextEm { get; set; } = 25;

    public override Size GetPreferredSize(Size proposedSize)
        => AiFgfWrap.Preferred(this, base.GetPreferredSize(proposedSize), MaxTextEm, Padding.Horizontal + 8, Padding.Vertical + 8);

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintedDirect3D = Enabled && Effects && PaintDirect3D(e);
        if (PaintedDirect3D) return;
        if (Enabled) { base.OnPaint(e); return; }

        AiFgfTheme t = AiFgfTheme.Current;
        Rectangle r = ClientRectangle;
        using (var face = new SolidBrush(t.App)) e.Graphics.FillRectangle(face, r);
        using (var border = new Pen(t.Border)) e.Graphics.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - 1);

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        Rectangle text = Rectangle.Inflate(r, -Padding.Left / 2, 0);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, t.Subtle, flags);
    }

    protected override void OnEnabledChanged(System.EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (!Enabled) { _animTimer?.Stop(); _hoverAnim = _hover ? 1f : 0f; _pressAnim = _pressed ? 1f : 0f; }
        UpdateMagic();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer?.Dispose();
#if NET11_0_OR_GREATER
            _magicTimer?.Dispose();
#endif
        }
        base.Dispose(disposing);
    }
}

/// <summary>A check box whose caption wraps past <see cref="MaxTextEm"/> em (see <see cref="AiFgfButton.MaxTextEm"/>).</summary>
public sealed class AiFgfCheck : CheckBox
{
    [System.ComponentModel.DefaultValue(25)]
    public int MaxTextEm { get; set; } = 25;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int glyph = LogicalToDeviceUnits(13) + LogicalToDeviceUnits(4);
        return AiFgfWrap.Preferred(this, base.GetPreferredSize(proposedSize), MaxTextEm, glyph + Padding.Horizontal + 4, Padding.Vertical + 2);
    }
#if NET11_0_OR_GREATER

    private AiFgfButtonStyle _look;
    private bool _hover;

    /// <summary>
    /// .NET 11: how the box is drawn - flat (the system's), or Raised / Glow: a sunken Direct3D well, and when checked a
    /// raised accent square with a tick (a dash when indeterminate). Caption, keyboard and focus stay the check box's;
    /// disabled or without Direct3D it is the system's. ToolForm and AiFgf.ApplyLook set it with the buttons' style.
    /// </summary>
    [System.ComponentModel.DefaultValue(AiFgfButtonStyle.Flat)]
    public AiFgfButtonStyle Look
    {
        get => _look;
        set { if (_look != value) { _look = value; Invalidate(); } }
    }

    /// <summary>True when the last paint drew the box with Direct3D (diagnostics, tests).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool PaintedDirect3D { get; private set; }

    /// <summary>The box (client coordinates): where the system draws its glyph, the caption to its right.</summary>
    public Rectangle GlyphBounds => AiFgfGlyph3D.Bounds(this);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); if (_look != AiFgfButtonStyle.Flat) Invalidate(); }

    protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); if (_look != AiFgfButtonStyle.Flat) Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);   // background, caption, focus cues - and the system's glyph, covered below
        PaintedDirect3D = _look != AiFgfButtonStyle.Flat && Enabled && AiFgfGlyph3D.Paint(this, e.Graphics, _look, round: false, CheckState, _hover);
    }
#endif
}

/// <summary>Preferred size of an auto-sized control whose caption wraps beyond a maximum width.</summary>
internal static class AiFgfWrap
{
    public static Size Preferred(Control c, Size natural, int maxEm, int chromeW, int chromeH)
    {
        if (!c.AutoSize || maxEm <= 0) return natural;
        int max = maxEm * c.Font.Height;
        // a check box or radio button: never wider than the wrapping row it sits in (its caption stuck out of a narrow
        // one); a bar's buttons keep their width — the window's minimum is one button wide
        if (c is CheckBox or RadioButton && c.Parent is FlowLayoutPanel { WrapContents: true } flow && flow.ClientSize.Width > 0)
            max = Math.Min(max, Math.Max(8 * c.Font.Height, flow.ClientSize.Width - flow.Padding.Horizontal - c.Margin.Horizontal));
        if (natural.Width <= max) return natural;
        Size text = TextRenderer.MeasureText(c.Text, c.Font, new Size(Math.Max(1, max - chromeW), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        return new Size(max, Math.Max(natural.Height, text.Height + chromeH));
    }
}
