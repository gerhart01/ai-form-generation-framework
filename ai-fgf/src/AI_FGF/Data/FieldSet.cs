using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// The named inputs of a form. Every row added through <see cref="FormScaffold.RowBuilder"/>
/// is registered here under a key (its label without the trailing colon, unless one is
/// given), so a form can read and write all of its values as a string dictionary — the
/// basis for settings persistence and JSON presets — and validate them in one call.
///
/// Validation is non-modal: invalid fields are tinted with <see cref="AiFgfTheme.Invalid"/> and
/// get a tooltip; the tint clears as soon as the user edits the field.
/// The model (keys, rules, conditions, values, validation) is <see cref="FieldSetBase{TSelf, TControl}"/>, shared
/// with the Avalonia FieldSet; this class is how WinForms controls are read, written, marked, enabled and hidden.
/// </summary>
public sealed class FieldSet : FieldSetBase<FieldSet, Control>
{
    private readonly ToolTip _tip = new();

    // ---------------------------------------------------------- rows

    private sealed class RowParts
    {
        public readonly List<Control> Companions = new();   // label, trailing button…
        public Action<bool>? SetHidden;                      // set by the layout that owns the row
    }

    private readonly Dictionary<string, RowParts> _rows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tell the set which controls belong to a field's row (its label, a Browse button…) so a
    /// condition can enable/hide the whole row; <paramref name="setHidden"/> lets the owning
    /// layout collapse the row instead of leaving a gap.
    /// </summary>
    public FieldSet AttachRow(string key, IEnumerable<Control> companions, Action<bool>? setHidden = null)
    {
        _ = this[key];
        RowParts r = _rows.TryGetValue(key, out var existing) ? existing : _rows[key] = new RowParts();
        r.Companions.AddRange(companions.Where(c => c is not null));
        r.SetHidden = setHidden;
        return this;
    }

    private IEnumerable<Control> Companions(string key) => _rows.TryGetValue(key, out var r) ? r.Companions : Enumerable.Empty<Control>();

    protected override void ApplyDisabled(string key, Control control, bool disabled)
    {
        control.Enabled = !disabled;
        foreach (Control c in Companions(key))
        {
            // A disabled Label is drawn embossed grey (about 2.4:1, unreadable on the
            // dark theme): keep it enabled and dim it to the theme's Subtle colour.
            if (c is Label label) Dim(label, disabled);
            else c.Enabled = !disabled;
        }
    }

    protected override void ApplyHidden(string key, Control control, bool hidden)
    {
        if (_rows.TryGetValue(key, out var r) && r.SetHidden is not null) { r.SetHidden(hidden); return; }
        control.Visible = !hidden;
        foreach (Control c in Companions(key)) c.Visible = !hidden;
    }

    // dimmed label → its ForeColor before it was dimmed
    // (with the theme current then: after AiFgfTheme.Switch the colour is mapped to the new theme)
    private readonly Dictionary<Label, (Color Color, AiFgfTheme Theme)> _dimmed = new();

    private void Dim(Label label, bool dim)
    {
        if (dim) { _dimmed.TryAdd(label, (label.ForeColor, AiFgfTheme.Current)); label.ForeColor = AiFgfTheme.Current.Subtle; }
        else if (_dimmed.Remove(label, out var original)) label.ForeColor = AiFgfTheme.Remap(original.Color, original.Theme, AiFgfTheme.Current, foreground: true);
    }

    // ---------------------------------------------------------- controls

    protected override void OnAdded(Control c)
    {
        void Clear(object? s, EventArgs e) => Mark(c, null);
        void Changed(object? s, EventArgs e) { if (HasConditions) Refresh(); }
        switch (c)
        {
            case CheckBox cb: cb.CheckedChanged += Clear; cb.CheckedChanged += Changed; break;
            case NumericUpDown n: n.ValueChanged += Clear; n.ValueChanged += Changed; break;
            case ComboBox combo: combo.TextChanged += Clear; combo.SelectedIndexChanged += Changed; combo.TextChanged += Changed; break;
            default: c.TextChanged += Clear; c.TextChanged += Changed; break;   // TextBox, MaskedTextBox, DateTimePicker, custom controls…
        }
    }

    protected override string ReadValue(Control c) => c switch
    {
        CheckBox cb => cb.Checked ? "true" : "false",
        NumericUpDown n => AiFgfNumbers.Format(n.Value, n.DecimalPlaces),   // as shown: "1.50", however the value was entered
        _ => c.Text,
    };

    protected override void WriteValue(Control c, string value)
    {
        switch (c)
        {
            case CheckBox cb:
                cb.Checked = bool.TryParse(value, out bool b) && b;
                break;
            case NumericUpDown n when AiFgfNumbers.TryParse(value, out decimal d):
                // round to what the control shows, or the value passed on differs from the one displayed
                n.Value = Math.Clamp(Math.Round(d, n.DecimalPlaces, MidpointRounding.AwayFromZero), n.Minimum, n.Maximum);
                break;
            case NumericUpDown:
                break; // unparsable number: keep the current value
            case ComboBox combo when combo.DropDownStyle == ComboBoxStyle.DropDownList:
                int idx = combo.FindStringExact(value);
                if (idx >= 0) combo.SelectedIndex = idx;
                break;
            default:
                c.Text = value;
                break;
        }
    }

    protected override decimal? SpinnerValue(Control c)
        => c is NumericUpDown n ? Math.Round(n.Value, n.DecimalPlaces, MidpointRounding.AwayFromZero) : null;

    protected override bool IsCheckBox(Control c) => c is CheckBox;

    protected override bool IsSecret(Control c) => c is TextBox { UseSystemPasswordChar: true } or TextBox { PasswordChar: not '\0' };   // a password box: never stored

    protected override void Focus(Control c) => c.Focus();

    // invalid control → the BackColor it had before it was tinted (restored when it becomes valid)
    private readonly Dictionary<Control, (Color Color, AiFgfTheme Theme)> _invalid = new();

    protected override bool IsMarked(Control c) => _invalid.ContainsKey(c);

    protected override void Mark(Control c, string? message)
    {
        // a check box is tinted too: a rule on it ("You must accept…") would otherwise show nothing
        if (message is null)
        {
            if (_invalid.Remove(c, out var original)) { c.BackColor = AiFgfTheme.Remap(original.Color, original.Theme, AiFgfTheme.Current, foreground: false); _tip.SetToolTip(c, null); }
            return;
        }
        _invalid.TryAdd(c, (c.BackColor, AiFgfTheme.Current));   // keep the colour from before the *first* tint
        c.BackColor = AiFgfTheme.Current.Invalid;
        _tip.SetToolTip(c, message);
    }
}
