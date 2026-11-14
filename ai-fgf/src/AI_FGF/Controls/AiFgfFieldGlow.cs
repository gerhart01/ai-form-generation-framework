using System;
using System.Drawing;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// Wraps a text or combo field with the same Direct3D 12 glow <see cref="AiFgfButton"/> draws
/// (<see cref="AiFgfDirect3D"/>) - a soft halo in the theme's accent colour that brightens as the field
/// gains focus - without touching the field's own native border or its text/caret/selection, which
/// stay the ordinary GDI edit control. Falls back to a plain background when Direct3D is unavailable,
/// the same way AiFgfButton falls back to flat.
/// <para>
/// Use <see cref="Wrap"/> after a row builder has placed the field (<c>rows.Text(label, out box, ...)</c>,
/// <c>rows.Combo(label, out combo, ...)</c>, <c>AiFgfMemo</c>, ...): it takes over the field's position -
/// same parent, Dock, Anchor, Bounds and tab index - and re-docks the field inside itself, inset by
/// a small margin so the halo has room to show.
/// </para>
/// </summary>
public sealed class AiFgfFieldGlow : Panel
{
    private readonly Control _inner;
    private readonly int _logicalMargin;
    private float _focusAnim;
    private System.Windows.Forms.Timer? _animTimer;
    private bool _hasFocus;

    /// <summary>The glow colour; empty (the default) follows the theme's accent.</summary>
    public Color GlowColor { get; set; } = Color.Empty;

    /// <summary>
    /// Also draw the field permanently sunken - AiFgfButton's own Raised face, pressed forced to 1 -
    /// under the focus halo, instead of a flat background. The sunken body ignores focus/hover (it
    /// is always inset); only the ring on top brightens, drawn in plain GDI+ since a second opaque
    /// Direct3D pass would just paint over the first one rather than blend with it.
    /// </summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool Sunken { get; set; }
    private bool ShouldSerializeGlowColor() => !GlowColor.IsEmpty;
    private void ResetGlowColor() => GlowColor = Color.Empty;

    private AiFgfFieldGlow(Control inner, int margin)
    {
        _inner = inner;
        _logicalMargin = margin;
        Padding = new Padding(margin);
        DoubleBuffered = true;
        BackColor = inner.BackColor;
        Controls.Add(inner);
        inner.Dock = DockStyle.Fill;
        inner.Enter += OnInnerEnter;
        inner.Leave += OnInnerLeave;
        inner.SizeChanged += OnInnerResized;
        inner.FontChanged += OnInnerResized;
    }

    /// <summary>Retrofit the glow onto <paramref name="inner"/> in place (same parent, Dock, Anchor, Bounds, tab index).</summary>
    public static AiFgfFieldGlow Wrap(Control inner, int margin = 4)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (inner.Parent is not { } parent) throw new InvalidOperationException("AiFgfFieldGlow.Wrap needs the field already placed in its parent.");

        int index = parent.Controls.GetChildIndex(inner);
        DockStyle dock = inner.Dock;
        AnchorStyles anchor = inner.Anchor;
        Rectangle bounds = inner.Bounds;
        Padding fieldMargin = inner.Margin;
        int tabIndex = inner.TabIndex;

        parent.Controls.Remove(inner);
        var glow = new AiFgfFieldGlow(inner, margin) { Dock = dock, Anchor = anchor, Bounds = bounds, Margin = fieldMargin, TabIndex = tabIndex };
        parent.Controls.Add(glow);
        parent.Controls.SetChildIndex(glow, index);
        // A ParamTable row remembers inner's own grid cell and would keep re-placing inner (now
        // orphaned inside glow) on every later layout pass, leaving glow wherever TableLayoutPanel's
        // own auto-placement happened to drop it - often a different row entirely. Hand the cell over.
        if (parent is ParamTable table) table.AdoptVisual(inner, glow);
        glow.FitToInner();
        return glow;
    }

    /// <summary>
    /// The height the field itself insists on, or -1 when it takes whatever height it is given. A
    /// single-line text box, combo or spinner cannot be made shorter than its font needs: docked inside
    /// the wrapper's padding at the field's own old height, it stuck out of the wrapper and was cut off.
    /// An <see cref="AiFgfMemo"/> asks for the height of its shown lines.
    /// </summary>
    private int NaturalHeight() => _inner switch
    {
        AiFgfMemo memo => memo.PreferredHeight,
        TextBoxBase { Multiline: true } => -1,
        ComboBox combo => combo.PreferredHeight,
        TextBoxBase or UpDownBase => _inner.GetPreferredSize(Size.Empty).Height,
        _ => -1,
    };

    /// <summary>Scale the inset to the DPI and make room for the field's natural height inside it.</summary>
    private void FitToInner()
    {
        if (_fitting) return;
        _fitting = true;
        try
        {
            int m = LogicalToDeviceUnits(_logicalMargin);
            if (Padding != new Padding(m)) Padding = new Padding(m);
            if (Dock is DockStyle.Fill or DockStyle.Left or DockStyle.Right) return;   // the parent decides the height
            int natural = NaturalHeight();
            if (natural > 0 && Height != natural + Padding.Vertical) Height = natural + Padding.Vertical;
        }
        finally { _fitting = false; }
    }

    private bool _fitting;

    private void OnInnerResized(object? sender, EventArgs e) => FitToInner();

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        FitToInner();
    }

    private void OnInnerEnter(object? sender, EventArgs e) { _hasFocus = true; Animate(); }
    private void OnInnerLeave(object? sender, EventArgs e) { _hasFocus = false; Animate(); }

    private void Animate()
    {
        _animTimer ??= new System.Windows.Forms.Timer { Interval = 15 };
        if (!_timerHooked) { _animTimer.Tick += (_, _) => StepAnimation(); _timerHooked = true; }
        if (!_animTimer.Enabled) _animTimer.Start();
        Invalidate();
    }

    private bool _timerHooked;

    private void StepAnimation()
    {
        const float Step = 0.25f;
        float target = _hasFocus ? 1f : 0f;
        _focusAnim = _focusAnim < target ? MathF.Min(target, _focusAnim + Step) : MathF.Max(target, _focusAnim - Step);
        Invalidate();
        if (_focusAnim == target) _animTimer!.Stop();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = DeviceDpi / 96f;
        Color back = Parent?.BackColor ?? AiFgfTheme.Current.App;
        if (back.A < 255) back = AiFgfTheme.Current.App;
        Color body = _inner.BackColor;
        Color glow = GlowColor.IsEmpty ? AiFgfTheme.Current.Accent : GlowColor;

        if (Sunken)
        {
            // the sunken body: AiFgfButton's own Raised face, pressed forced to 1 so it is always inset
            Bitmap? sunk = AiFgfDirect3D.Draw(new AiFgfDirect3D.Face(Width, Height, scale, body, back, Color.Transparent, 0f, 1f));
            if (sunk is not null) e.Graphics.DrawImageUnscaled(sunk, 0, 0);
            else { using var flat = new SolidBrush(body); e.Graphics.FillRectangle(flat, ClientRectangle); }

            if (_focusAnim > 0.01f)
            {
                int alpha = (int)(235 * _focusAnim);
                float width = Math.Max(1.5f, 2.5f * scale);
                using var pen = new Pen(Color.FromArgb(alpha, glow), width);
                var prior = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float inset = width / 2 + 1;
                e.Graphics.DrawRectangle(pen, inset, inset, Width - 2 * inset, Height - 2 * inset);
                e.Graphics.SmoothingMode = prior;
            }
            return;
        }

        Bitmap? face = AiFgfDirect3D.Draw(new AiFgfDirect3D.Face(Width, Height, scale, body, back, Color.Transparent, _focusAnim, 0f, glow, _hasFocus));
        if (face is not null) { e.Graphics.DrawImageUnscaled(face, 0, 0); return; }
        using var b = new SolidBrush(body);
        e.Graphics.FillRectangle(b, ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer?.Dispose();
            _inner.Enter -= OnInnerEnter;
            _inner.Leave -= OnInnerLeave;
            _inner.SizeChanged -= OnInnerResized;
            _inner.FontChanged -= OnInnerResized;
        }
        base.Dispose(disposing);
    }
}
