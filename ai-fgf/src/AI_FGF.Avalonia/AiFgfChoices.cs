using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AI_FGF.Avalonia;

/// <summary>
/// One of a few fixed choices, all shown at once as radio buttons that wrap like a row of check boxes (the
/// WinForms AiFgfRadioGroup). <see cref="FieldSet"/> reads and writes the selected item, as for a drop-down choice.
/// </summary>
public sealed class AiFgfRadioGroup : WrapPanel
{
    private readonly List<RadioButton> _buttons = new();
    private readonly List<string> _items;

    public AiFgfRadioGroup(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToList();
        if (_items.Count == 0) throw new ArgumentException("A radio group needs at least one item.", nameof(items));
        Orientation = Orientation.Horizontal;
        Margin = WinFormsMetrics.ChecksRowMargin;
        string group = "ai-fgf-radio-" + Guid.NewGuid().ToString("N");
        foreach (string item in _items)
        {
            var b = AiFgf.CapWidth(new RadioButton
            {
                // the caption is the value: no access key ("Fish & Chips" stays as written)
                Content = new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap },
                GroupName = group,
                Margin = WinFormsMetrics.CheckMargin,
                VerticalAlignment = VerticalAlignment.Center,
            }, AiFgf.MaxCaptionEm);
            b.IsCheckedChanged += (_, _) => { if (b.IsChecked == true) SelectionChanged?.Invoke(this, EventArgs.Empty); };
            _buttons.Add(b);
            Children.Add(b);
        }
        _buttons[0].IsChecked = true;
    }

    /// <summary>Raised when another item is selected.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The items, in order.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>The buttons, in order.</summary>
    public IReadOnlyList<RadioButton> Buttons => _buttons;

    /// <summary>The selected item's index (one is always selected).</summary>
    public int SelectedIndex
    {
        get => _buttons.FindIndex(b => b.IsChecked == true);
        set => _buttons[value].IsChecked = true;
    }

    /// <summary>The selected item; setting an item that is not offered keeps the selection.</summary>
    public string SelectedItem
    {
        get => SelectedIndex is int i and >= 0 ? _items[i] : "";
        set { int i = _items.IndexOf(value); if (i >= 0) SelectedIndex = i; }
    }
}

/// <summary>A whole number picked on a slider (the WinForms AiFgfSlider); its value is shown beside it.</summary>
public sealed class AiFgfSlider : Slider
{
    protected override Type StyleKeyOverride => typeof(Slider);

    public AiFgfSlider()
    {
        IsSnapToTickEnabled = true;
        TickFrequency = 1;
        SmallChange = 1;
        MinWidth = 120;
        Margin = WinFormsMetrics.InputMargin;   // a row apart from its neighbours, as every input
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>The value as a whole number.</summary>
    public int IntValue
    {
        get => (int)Math.Round(Value);
        set => Value = Math.Clamp(value, Minimum, Maximum);
    }

    /// <summary>The value as <see cref="FieldSet"/> reads it ("40").</summary>
    public string Text => IntValue.ToString(CultureInfo.InvariantCulture);
}
