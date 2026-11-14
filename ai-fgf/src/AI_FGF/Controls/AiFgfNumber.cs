using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// A <see cref="NumericUpDown"/> whose spin arrows are drawn in the theme's colours. The stock
/// arrows are painted by the light system style whatever the theme — two white squares in a dark
/// window. Here the face is the field colour (hover/press tinted), the arrows the text colour,
/// with a border-coloured divider; behaviour (clicks, auto-repeat, keyboard, wheel) is unchanged.
/// </summary>
public sealed class AiFgfNumber : NumericUpDown
{
    private readonly Control? _buttons;
    private int _hover = -1, _pressed = -1;   // 0 = up, 1 = down

    public AiFgfNumber()
    {
        // the spin buttons are WinForms' internal UpDownButtons child; it raises Paint after its own drawing
        _buttons = Controls.Cast<Control>().FirstOrDefault(c => c is not TextBoxBase);
        if (_buttons is null) return;
        _buttons.Paint += PaintButtons;
        _buttons.MouseMove += (_, e) => SetState(ref _hover, Half(e.Y));
        _buttons.MouseLeave += (_, _) => SetState(ref _hover, -1);
        _buttons.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) SetState(ref _pressed, Half(e.Y)); };
        _buttons.MouseUp += (_, _) => SetState(ref _pressed, -1);
    }

    /// <summary>The spin-button child (for tests).</summary>
    public Control? SpinButtons => _buttons;

    private int Half(int y) => _buttons is null ? -1 : y < _buttons.Height / 2 ? 0 : 1;

    private void SetState(ref int field, int value)
    {
        if (field == value) return;
        field = value;
        _buttons?.Invalidate();
    }

#if NET11_0_OR_GREATER
    /// <summary>
    /// .NET 11: how the spin buttons are drawn. Raised and Glow draw each half as a small Direct3D face (<see cref="AiFgfDirect3D"/>):
    /// raised and lit from above, brighter under the pointer, sunk while pressed (Glow: the hovered or pressed half glows in
    /// the accent). Flat, disabled, or without a working Direct3D 12, the flat halves. ToolForm sets it with the buttons' style.
    /// </summary>
    [System.ComponentModel.DefaultValue(AiFgfButtonStyle.Flat)]
    public AiFgfButtonStyle Look
    {
        get => _look;
        set { if (_look != value) { _look = value; _buttons?.Invalidate(); } }
    }

    private AiFgfButtonStyle _look;

    /// <summary>True when the last paint drew the spin buttons with Direct3D (diagnostics, tests).</summary>
    public bool PaintedDirect3D { get; private set; }

    /// <summary>Show the hover / pressed half without a mouse (tests, screenshots): 0 = up, 1 = down, -1 = none.</summary>
    public void SetSpinState(int hover, int pressed) { _hover = hover; _pressed = pressed; _buttons?.Invalidate(); }

    private bool PaintFacesDirect3D(Graphics g, Rectangle[] halves, AiFgfTheme t)
    {
        float scale = DeviceDpi / 96f;
        bool glow = _look == AiFgfButtonStyle.Glow;
        var faces = new Bitmap[2];
        for (int i = 0; i < 2; i++)
        {
            Rectangle h = halves[i];
            bool lit = _hover == i || _pressed == i;
            // the theme's state fills, as on the flat halves: the shader only lightens a hovered face, and a white field's
            // face cannot get lighter - hovering showed nothing in the light theme
            Color body = _pressed == i ? t.PressFill : _hover == i ? t.HoverFill : t.Surface;
            Bitmap? face = AiFgfDirect3D.Draw(new AiFgfDirect3D.Face(h.Width, h.Height, scale, body, t.Surface, Color.Transparent,
                _hover == i ? 1f : 0f, _pressed == i ? 1f : 0f, glow && lit ? t.Accent : default));
            if (face is null) return false;
            faces[i] = face;
        }
        for (int i = 0; i < 2; i++) g.DrawImageUnscaled(faces[i], halves[i].X, halves[i].Y);
        return true;
    }
#endif

    private void PaintButtons(object? sender, PaintEventArgs e)
    {
        if (_buttons is null) return;
        AiFgfTheme t = AiFgfTheme.Current;
        Graphics g = e.Graphics;
        Rectangle r = _buttons.ClientRectangle;
        int mid = r.Height / 2;
        var halves = new[] { new Rectangle(r.X, r.Y, r.Width, mid), new Rectangle(r.X, r.Y + mid, r.Width, r.Height - mid) };

        bool faces3D = false;
#if NET11_0_OR_GREATER
        faces3D = PaintedDirect3D = Enabled && _look != AiFgfButtonStyle.Flat && halves[0].Width >= 4 && halves[0].Height >= 4 && PaintFacesDirect3D(g, halves, t);
#endif
        if (!faces3D)
            for (int i = 0; i < 2; i++)
            {
                Color face = !Enabled ? t.App : _pressed == i ? t.PressFill : _hover == i ? t.HoverFill : t.Surface;
                using (var fill = new SolidBrush(face)) g.FillRectangle(fill, halves[i]);
            }
        using (var line = new Pen(t.Border))
        {
            g.DrawLine(line, r.X, r.Y, r.X, r.Bottom - 1);           // divider from the text
            if (!faces3D) g.DrawLine(line, r.X, r.Y + mid, r.Right - 1, r.Y + mid); // between up and down (the faces have their own edges)
        }

        Color arrow = Enabled ? t.Text : t.Subtle;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(arrow);
        for (int i = 0; i < 2; i++)
        {
            Rectangle h = halves[i];
            float w = Math.Max(4f, Math.Min(h.Width * 0.5f, h.Height * 0.9f));   // scales with DPI
            float half = w / 2, tall = w / 2;
            float cx = h.X + 1 + (h.Width - 1) / 2f, cy = h.Y + h.Height / 2f;
            PointF[] tri = i == 0
                ? new[] { new PointF(cx - half, cy + tall / 2), new PointF(cx + half, cy + tall / 2), new PointF(cx, cy - tall / 2) }
                : new[] { new PointF(cx - half, cy - tall / 2), new PointF(cx + half, cy - tall / 2), new PointF(cx, cy + tall / 2) };
            g.FillPolygon(brush, tri);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _buttons?.Invalidate();
    }
}
