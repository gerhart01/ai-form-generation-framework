using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>A radio button whose long caption wraps beyond <see cref="MaxTextEm"/> (as <see cref="AiFgfCheck"/>).</summary>
public sealed class AiFgfRadio : RadioButton
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
    /// .NET 11: how the button's circle is drawn - flat (the system's), or Raised / Glow: a sunken Direct3D well with a
    /// raised accent dot when selected. The caption, keyboard and grouping stay the radio button's; disabled or without
    /// Direct3D it is the system's. ToolForm and AiFgf.ApplyLook set it with the buttons' style.
    /// </summary>
    [System.ComponentModel.DefaultValue(AiFgfButtonStyle.Flat)]
    public AiFgfButtonStyle Look
    {
        get => _look;
        set { if (_look != value) { _look = value; Invalidate(); } }
    }

    /// <summary>True when the last paint drew the circle with Direct3D (diagnostics, tests).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool PaintedDirect3D { get; private set; }

    /// <summary>The circle (client coordinates): where the system draws its glyph, the caption to its right.</summary>
    public Rectangle GlyphBounds => AiFgfGlyph3D.Bounds(this);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); if (_look != AiFgfButtonStyle.Flat) Invalidate(); }

    protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); if (_look != AiFgfButtonStyle.Flat) Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);   // background, caption, focus cues - and the system's glyph, covered below
        PaintedDirect3D = _look != AiFgfButtonStyle.Flat && Enabled
            && AiFgfGlyph3D.Paint(this, e.Graphics, _look, round: true, Checked ? CheckState.Checked : CheckState.Unchecked, _hover);
    }
#endif
}

/// <summary>
/// One of a few fixed choices, all shown at once as radio buttons that wrap like a row of check boxes.
/// Its <see cref="Text"/> is the selected item — what <see cref="FieldSet"/> reads and writes, as for a
/// drop-down choice — and <see cref="Control.TextChanged"/> fires when the selection changes.
/// </summary>
public sealed class AiFgfRadioGroup : FlowLayoutPanel
{
    private readonly List<RadioButton> _buttons = new();

    public AiFgfRadioGroup(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        AutoSize = false;   // ParamTable gives a flow panel the wrapped height it needs
        WrapContents = true;
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        Margin = new Padding(0, 2, 0, 2);
        foreach (string item in items)
        {
            var b = new AiFgfRadio
            {
                Text = item,
                AutoSize = true,
                UseMnemonic = false,   // the caption is the value: "Fish & Chips" stays as written
                Margin = new Padding(0, 6, 16, 0),
                ForeColor = AiFgfTheme.Current.Text,
            };
            b.CheckedChanged += (_, _) => { if (b.Checked) OnTextChanged(EventArgs.Empty); };
            _buttons.Add(b);
            Controls.Add(b);
        }
        if (_buttons.Count == 0) throw new ArgumentException("A radio group needs at least one item.", nameof(items));
        _buttons[0].Checked = true;
    }

    /// <summary>The items, in order.</summary>
    public IReadOnlyList<string> Items => _buttons.Select(b => b.Text).ToList();

    /// <summary>The buttons, in order.</summary>
    public IReadOnlyList<RadioButton> Buttons => _buttons;

    /// <summary>The selected item's index (one is always selected).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _buttons.FindIndex(b => b.Checked);
        set => _buttons[value].Checked = true;
    }

    /// <summary>The selected item; setting an item that is not offered keeps the selection.</summary>
    [AllowNull]
    public override string Text
    {
        get => SelectedIndex is int i and >= 0 ? _buttons[i].Text : "";
        set { int i = _buttons.FindIndex(b => b.Text == value); if (i >= 0) SelectedIndex = i; }
    }
}

/// <summary>
/// A whole number picked on a slider: a track bar exactly as high as its thumb (no ticks — the value is
/// shown beside it). Its <see cref="Text"/> is the value ("40"), so <see cref="FieldSet"/> reads and writes
/// it like any field; <see cref="Control.TextChanged"/> fires when it moves.
/// </summary>
public sealed class AiFgfSlider : TrackBar
{
    public AiFgfSlider()
    {
        TickStyle = TickStyle.None;
        AutoSize = true;
        SmallChange = 1;
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
        BackColor = AiFgfTheme.Current.App;
        AiFgf.FitTrackBarToThumb(this);
    }

    [AllowNull]
    public override string Text
    {
        get => Value.ToString(CultureInfo.InvariantCulture);
        set { if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) Value = Math.Clamp(v, Minimum, Maximum); }
    }

    protected override void OnValueChanged(EventArgs e)
    {
        base.OnValueChanged(e);
        OnTextChanged(EventArgs.Empty);
#if NET11_0_OR_GREATER
        if (_look != AiFgfButtonStyle.Flat) Invalidate();   // the accent part of the groove grows or shrinks with the value
#endif
    }
#if NET11_0_OR_GREATER

    private AiFgfButtonStyle _look;

    /// <summary>
    /// .NET 11: how the slider is drawn - flat (the system track bar), or Raised / Glow: a sunken Direct3D groove,
    /// accent-coloured up to the thumb (lit with Glow), and a raised Direct3D thumb. Disabled, or without Direct3D,
    /// it is the system's. ToolForm sets it with the buttons' style.
    /// </summary>
    [System.ComponentModel.DefaultValue(AiFgfButtonStyle.Flat)]
    public AiFgfButtonStyle Look
    {
        get => _look;
        set { if (_look != value) { _look = value; Invalidate(); } }
    }

    /// <summary>True when the last paint drew the groove and thumb with Direct3D (diagnostics, tests).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool PaintedDirect3D { get; private set; }

    /// <summary>Where the thumb is now (client coordinates).</summary>
    public Rectangle ThumbBounds => IsHandleCreated ? AiFgf.ThumbRect(this) : Rectangle.Empty;

    /// <summary>The groove as the 3D look draws it (client coordinates): a pill through the thumb's middle.</summary>
    public Rectangle GrooveBounds
    {
        get
        {
            Rectangle thumb = AiFgf.ThumbRect(this);
            int h = Math.Max(4, LogicalToDeviceUnits(6)), mid = thumb.Top + thumb.Height / 2;
            int edge = LogicalToDeviceUnits(4);
            return new Rectangle(edge, mid - h / 2, Math.Max(h, ClientSize.Width - 2 * edge), h);
        }
    }

    // custom draw (comctl32 NM_CUSTOMDRAW, reflected to the control by WinForms): the groove and the thumb are drawn
    // here, the rest - background, focus rectangle, keyboard and mouse - stays the system track bar's
    private const int WM_REFLECT_NOTIFY = 0x2000 + 0x004E, NM_CUSTOMDRAW = -12;
    private const int CDDS_PREPAINT = 0x1, CDDS_ITEMPREPAINT = 0x10001;
    private const int CDRF_DODEFAULT = 0x0, CDRF_SKIPDEFAULT = 0x4, CDRF_NOTIFYITEMDRAW = 0x20;
    private const int TBCD_THUMB = 2, TBCD_CHANNEL = 3;
    private const uint CDIS_SELECTED = 0x1, CDIS_HOT = 0x40;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NMHDR { public IntPtr hwndFrom; public UIntPtr idFrom; public int code; }

    // the header is its own struct: on x64 it is padded to 24 bytes, and the draw stage comes after the padding
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NMCUSTOMDRAW
    {
        public NMHDR hdr;
        public int dwDrawStage; public IntPtr hdc; public int left, top, right, bottom;
        public IntPtr dwItemSpec; public uint uItemState; public IntPtr lItemlParam;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_REFLECT_NOTIFY)
        {
            var cd = System.Runtime.InteropServices.Marshal.PtrToStructure<NMCUSTOMDRAW>(m.LParam);
            if (cd.hdr.code == NM_CUSTOMDRAW)
            {
                if (_look == AiFgfButtonStyle.Flat || !Enabled) { if (cd.dwDrawStage == CDDS_PREPAINT) PaintedDirect3D = false; }
                else if (cd.dwDrawStage == CDDS_PREPAINT) { m.Result = CDRF_NOTIFYITEMDRAW; return; }
                else if (cd.dwDrawStage == CDDS_ITEMPREPAINT && (int)cd.dwItemSpec is TBCD_CHANNEL or TBCD_THUMB)
                {
                    bool drawn;
                    using (Graphics g = Graphics.FromHdc(cd.hdc))
                        drawn = (int)cd.dwItemSpec == TBCD_CHANNEL ? PaintGroove(g) : PaintThumb(g, Rectangle.FromLTRB(cd.left, cd.top, cd.right, cd.bottom), cd.uItemState);
                    PaintedDirect3D = drawn;
                    m.Result = drawn ? CDRF_SKIPDEFAULT : CDRF_DODEFAULT;
                    return;
                }
            }
        }
        base.WndProc(ref m);
    }

    private bool PaintGroove(Graphics g)
    {
        AiFgfTheme t = AiFgfTheme.Current;
        Rectangle groove = GrooveBounds, thumb = AiFgf.ThumbRect(this);
        float scale = DeviceDpi / 96f, radius = groove.Height / 2f;
        Color back = BackColor.A == 255 ? BackColor : t.App, off = AiFgfColor.Mix(t.App, t.Border, 0.6);
        // sunken (Pressed = 1): the empty part in the off colour, the filled part - up to the thumb's middle - in the accent
        Bitmap? empty = AiFgfDirect3D.Draw(new AiFgfFace(groove.Width, groove.Height, scale, off, back, t.Border, 0f, 1f, Radius: radius));
        // with Glow the filled part is lit: the accent lightened (a halo would not fit a groove this thin)
        Color fill = _look == AiFgfButtonStyle.Glow ? AiFgfColor.Mix(t.Accent, Color.White, 0.2) : t.Accent;
        Bitmap? full = AiFgfDirect3D.Draw(new AiFgfFace(groove.Width, groove.Height, scale, fill, back, t.Border, 0f, 1f, Radius: radius));
        if (empty is null || full is null) return false;
        int split = Math.Clamp(thumb.Left + thumb.Width / 2 - groove.X, 0, groove.Width);
        g.DrawImage(full, new Rectangle(groove.X, groove.Y, split, groove.Height), new Rectangle(0, 0, split, groove.Height), GraphicsUnit.Pixel);
        g.DrawImage(empty, new Rectangle(groove.X + split, groove.Y, groove.Width - split, groove.Height), new Rectangle(split, 0, groove.Width - split, groove.Height), GraphicsUnit.Pixel);
        return true;
    }

    private bool PaintThumb(Graphics g, Rectangle thumb, uint state)
    {
        if (thumb.Width < 4 || thumb.Height < 4) return false;
        AiFgfTheme t = AiFgfTheme.Current;
        float scale = DeviceDpi / 96f;
        Color back = BackColor.A == 255 ? BackColor : t.App;
        // never a halo: it would take the room of a thumb this narrow. A raised vertical pill (the thumb's own rectangle: the track bar repaints only that as it moves); the corners
        // outside the pill take the window colour, so the groove shows up to the thumb's edge
        Bitmap? face = AiFgfDirect3D.Draw(new AiFgfFace(thumb.Width, thumb.Height, scale, t.Surface, back, t.Border,
            (state & CDIS_HOT) != 0 ? 1f : 0f, (state & CDIS_SELECTED) != 0 ? 1f : 0f, Radius: thumb.Width / 2f));
        if (face is null) return false;
        g.DrawImageUnscaled(face, thumb.X, thumb.Y);
        return true;
    }
#endif
}
