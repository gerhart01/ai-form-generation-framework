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
    }
}
