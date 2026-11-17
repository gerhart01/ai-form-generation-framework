#if NET11_0_OR_GREATER
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// .NET 11: a note - a line of text in a form (a hint, a status line carried over from an old form; <c>"kind": "note"</c>).
/// Flat it is a subtle label. With <see cref="Look"/> Raised or Glow it is a plate: a sunken Direct3D strip with a raised
/// accent bar at its start (lit with Glow) and the text in the theme's text colour - set apart from the inputs around it.
/// Disabled or without Direct3D it is the flat label again. ToolForm and <see cref="AiFgf.ApplyLook"/> set the look.
/// </summary>
public sealed class AiFgfNote : Label
{
    private AiFgfButtonStyle _look;

    public AiFgfNote()
    {
        AutoSize = true;
        UseMnemonic = false;   // the text as written: "Q&A" stays "Q&A"
        ForeColor = AiFgfTheme.Current.Subtle;
        Margin = new Padding(0, 6, 6, 0);
    }

    /// <summary>Flat (a subtle label), or Raised / Glow: the Direct3D plate.</summary>
    [DefaultValue(AiFgfButtonStyle.Flat)]
    public AiFgfButtonStyle Look
    {
        get => _look;
        set
        {
            if (_look == value) return;
            _look = value;
            ApplyPadding();
            Invalidate();
        }
    }

    /// <summary>True when the last paint drew the plate with Direct3D (diagnostics, tests).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool PaintedDirect3D { get; private set; }

    /// <summary>The accent bar at the plate's start (client coordinates; right to left, at its end).</summary>
    public Rectangle BarBounds
    {
        get
        {
            int inset = LogicalToDeviceUnits(4), w = Math.Max(3, LogicalToDeviceUnits(4));
            int x = RightToLeft == RightToLeft.Yes ? ClientSize.Width - inset - w : inset;
            return new Rectangle(x, inset, w, Math.Max(0, ClientSize.Height - 2 * inset));
        }
    }

    // the plate's room: the bar and some air before the text, a little above and below it (in device pixels: set again
    // after a DPI change)
    private void ApplyPadding()
    {
        int start = LogicalToDeviceUnits(14), end = LogicalToDeviceUnits(8), v = LogicalToDeviceUnits(5);
        Padding = _look == AiFgfButtonStyle.Flat ? Padding.Empty
            : RightToLeft == RightToLeft.Yes ? new Padding(end, v, start, v) : new Padding(start, v, end, v);
    }

    // after a DPI change WinForms scales the padding itself; set it again so it is exactly the plate's for the new DPI
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { base.ScaleControl(factor, specified); ApplyPadding(); }

    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ApplyPadding(); }

    private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

    /// <summary>
    /// The plate's size: the text wrapped at the room left inside the padding (MaximumSize, set by the table it sits in, is
    /// the whole plate). A label wrapped at the whole width and then added the padding - wider than its column, which
    /// widened the table past the window, row after row.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (_look == AiFgfButtonStyle.Flat) return base.GetPreferredSize(proposedSize);
        int max = MaximumSize.Width > 0 ? MaximumSize.Width : proposedSize.Width is > 1 and < int.MaxValue ? proposedSize.Width : int.MaxValue;
        int room = max == int.MaxValue ? int.MaxValue : Math.Max(1, max - Padding.Horizontal);
        Size text = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(room, int.MaxValue), Wrap);
        return new Size(Math.Min(max, text.Width + Padding.Horizontal), text.Height + Padding.Vertical);
    }

    protected override void OnRightToLeftChanged(EventArgs e) { base.OnRightToLeftChanged(e); ApplyPadding(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintedDirect3D = _look != AiFgfButtonStyle.Flat && Enabled && PaintPlate(e.Graphics);
        if (!PaintedDirect3D) base.OnPaint(e);
    }

    private bool PaintPlate(Graphics g)
    {
        if (ClientSize.Width < 8 || ClientSize.Height < 8) return false;
        AiFgfTheme t = AiFgfTheme.Current;
        float scale = DeviceDpi / 96f;
        Color body = Body(t);
        Bitmap? plate = AiFgfDirect3D.Draw(new AiFgfFace(ClientSize.Width, ClientSize.Height, scale, body, Background(), t.Border, 0f, 1f, Radius: 4f * scale));
        if (plate is null) return false;
        Rectangle bar = BarBounds;
        Bitmap? accent = bar.Height >= 4
            ? AiFgfDirect3D.Draw(new AiFgfFace(bar.Width, bar.Height, scale, AiFgfGlyph3D.OnColour(_look), body, Color.Transparent, 0f, 0f, Radius: bar.Width / 2f))
            : null;
        g.DrawImageUnscaled(plate, 0, 0);
        if (accent is not null) g.DrawImageUnscaled(accent, bar.X, bar.Y);
        var text = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, ClientSize.Width - Padding.Horizontal), Math.Max(1, ClientSize.Height - Padding.Vertical));
        var flags = Wrap | TextFormatFlags.VerticalCenter
                    | (RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
        TextRenderer.DrawText(g, Text, Font, text, t.Text, flags);
        return true;
    }

    /// <summary>The plate's colour: between the window and the inputs, so it reads as part of the form, not an input.</summary>
    public static Color Body(AiFgfTheme t) => AiFgfColor.Mix(t.App, t.Surface, 0.55);

    private Color Background()
    {
        for (Control? p = Parent; p is not null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return AiFgfTheme.Current.App;
    }
}
#endif
