using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AI_FGF.Avalonia;

/// <summary>
/// The named inputs of a form — the same API as the WinForms <c>FieldSet</c>: values by key
/// (strings; checkboxes "true"/"false"; numbers invariant), typed reads (<see cref="FieldSetBase{TSelf, TControl}.Text"/>,
/// <see cref="FieldSetBase{TSelf, TControl}.Int"/>, <see cref="FieldSetBase{TSelf, TControl}.Decimal"/>, …),
/// required fields and rules, non-modal validation (invalid fields marked with an error tip, the
/// first focused), and conditions: EnableWhen (disabled, label dimmed — still readable) and VisibleWhen (row hidden,
/// takes no space). Inactive fields are neither validated nor in GetActiveValues.
/// The model is <see cref="FieldSetBase{TSelf, TControl}"/>, shared with WinForms; this class is how Avalonia
/// controls are read, written, marked, enabled and hidden.
/// </summary>
public sealed class FieldSet : FieldSetBase<FieldSet, Control>
{
    private readonly Dictionary<string, List<Control>> _companions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Control> _invalid = new();

    /// <summary>The label, trailing button… that go with the field (hidden/disabled with it).</summary>
    public FieldSet AttachRow(string key, IEnumerable<Control> companions)
    {
        _ = this[key];
        if (!_companions.TryGetValue(key, out var list)) _companions[key] = list = new List<Control>();
        list.AddRange(companions.Where(c => c is not null));
        return this;
    }

    private IEnumerable<Control> Companions(string key) => _companions.TryGetValue(key, out var l) ? l : Enumerable.Empty<Control>();

    protected override void ApplyDisabled(string key, Control control, bool disabled)
    {
        control.IsEnabled = !disabled;
        foreach (Control c in Companions(key))
        {
            // a label is dimmed, not disabled: disabled text is hard to read
            if (c is Label or TextBlock) Dim(c, disabled);
            else c.IsEnabled = !disabled;
        }
    }

    protected override void ApplyHidden(string key, Control control, bool hidden)
    {
        control.IsVisible = !hidden;
        foreach (Control c in Companions(key)) c.IsVisible = !hidden;
    }

    private static void Dim(Control label, bool dim)
    {
        var property = label is TemplatedControl ? TemplatedControl.ForegroundProperty : TextBlock.ForegroundProperty;
        if (dim) AvTheme.Bind(label, property, AvTheme.SubtleKey);
        else label.ClearValue(property);   // (not null: a null Foreground means no brush — invisible text)
    }

    protected override void OnAdded(Control c)
    {
        void Changed() { Mark(c, null); Refresh(); }
        switch (c)
        {
            case ToggleButton cb: cb.IsCheckedChanged += (_, _) => Changed(); break;   // a check box, or a switch (.NET 11)
            case NumericUpDown n: n.ValueChanged += (_, _) => Changed(); break;
            case ComboBox combo:
                combo.SelectionChanged += (_, _) => Changed();
                combo.PropertyChanged += (_, e) => { if (e.Property == ComboBox.TextProperty) Changed(); };
                break;
            case TextBox t: t.TextChanged += (_, _) => Changed(); break;
            case AutoCompleteBox a: a.TextChanged += (_, _) => Changed(); break;
            case AiFgfRadioGroup g: g.SelectionChanged += (_, _) => Changed(); break;
            case AiFgfSlider s: s.ValueChanged += (_, _) => Changed(); break;
        }
    }

    protected override string ReadValue(Control c) => c switch
    {
        ToggleButton cb => cb.IsChecked == true ? "true" : "false",
        AiFgfNumber n => AiFgfNumbers.Format(n.Value ?? n.Minimum, n.DecimalPlaces),   // as shown: "1.50", however the value was entered
        NumericUpDown n => (n.Value ?? n.Minimum).ToString(CultureInfo.InvariantCulture),
        ComboBox { IsEditable: true } combo => combo.Text ?? "",
        ComboBox combo => combo.SelectedItem?.ToString() ?? "",
        TextBox t => t.Text ?? "",
        AutoCompleteBox a => a.Text ?? "",
        AiFgfRadioGroup g => g.SelectedItem,
        AiFgfSlider s => s.Text,
        ContentControl cc => AiFgf.Caption(cc.Content),
        _ => "",
    };

    protected override void WriteValue(Control c, string value)
    {
        switch (c)
        {
            case ToggleButton cb:
                cb.IsChecked = bool.TryParse(value, out bool b) && b;
                break;
            case NumericUpDown n when AiFgfNumbers.TryParse(value, out decimal d):
                int dec = n is AiFgfNumber f ? f.DecimalPlaces : 10;
                n.Value = Math.Clamp(Math.Round(d, dec, MidpointRounding.AwayFromZero), n.Minimum, n.Maximum);
                break;
            case NumericUpDown:
                break; // unparsable number: keep the current value
            case ComboBox { IsEditable: true } combo:
                combo.Text = value;
                break;
            case ComboBox combo:
                object? item = combo.Items.Cast<object?>().FirstOrDefault(i => string.Equals(i?.ToString(), value, StringComparison.Ordinal));
                if (item is not null) combo.SelectedItem = item;   // an unknown value keeps the selection
                break;
            case TextBox t: t.Text = value; break;
            case AutoCompleteBox a: a.Text = value; break;
            case AiFgfRadioGroup g: g.SelectedItem = value; break;   // an unknown value keeps the selection
            case AiFgfSlider s when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v): s.IntValue = v; break;
        }
    }

    protected override decimal? SpinnerValue(Control c) => c switch
    {
        AiFgfNumber f => Math.Round(f.Value ?? f.Minimum, f.DecimalPlaces, MidpointRounding.AwayFromZero),
        NumericUpDown n => n.Value ?? n.Minimum,
        _ => null,
    };

    protected override bool IsCheckBox(Control c) => c is ToggleButton;

    protected override bool IsSecret(Control c) => c is TextBox { PasswordChar: not '\0' };   // a password box: never stored

    protected override void Focus(Control c) => c.Focus();

    protected override bool IsMarked(Control c) => _invalid.Contains(c);

    protected override void Mark(Control c, string? message)
    {
        if (message is null)
        {
            if (_invalid.Remove(c)) { DataValidationErrors.ClearErrors(c); ToolTip.SetTip(c, null); c.ClearValue(TemplatedControl.BackgroundProperty); }
            return;
        }
        _invalid.Add(c);
        if (c is TemplatedControl tc and not ToggleButton) tc.Background = AvTheme.Brush(AiFgfTheme.Current.Invalid);   // the WinForms tint
        DataValidationErrors.SetErrors(c, new object[] { message });
        ToolTip.SetTip(c, message);
    }
}
