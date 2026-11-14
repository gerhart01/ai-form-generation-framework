using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
/// </summary>
public sealed class FieldSet
{
    private readonly List<(string Key, Control Control)> _fields = new();
    private readonly HashSet<string> _required = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string, string?>> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolTip _tip = new();

    /// <summary>All keys, in the order the fields were added.</summary>
    public IReadOnlyList<string> Keys => _fields.Select(f => f.Key).ToList();

    /// <summary>The control registered under <paramref name="key"/>.</summary>
    public Control this[string key] => Find(key) ?? throw new KeyNotFoundException($"No field '{key}'.");

    public bool Contains(string key) => Find(key) is not null;

    /// <summary>Register a control. Keys are case-insensitive and must be unique.</summary>
    public FieldSet Add(string key, Control control, bool required = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(control);
        if (Contains(key)) throw new ArgumentException($"Duplicate field key '{key}'.", nameof(key));
        _fields.Add((key, control));
        if (required) _required.Add(key);
        HookClearOnEdit(control);
        HookValueChanged(control);
        return this;
    }

    // ---------------------------------------------------------- conditions

    private sealed class RowParts
    {
        public readonly List<Control> Companions = new();   // label, trailing button…
        public Action<bool>? SetHidden;                      // set by the layout that owns the row
        public Func<FieldSet, bool>? EnabledWhen, VisibleWhen;
        public bool Disabled, Hidden;
    }

    private readonly Dictionary<string, RowParts> _rows = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshing;

    private RowParts Row(string key) { _ = this[key]; return _rows.TryGetValue(key, out var r) ? r : _rows[key] = new RowParts(); }

    /// <summary>
    /// Tell the set which controls belong to a field's row (its label, a Browse button…) so a
    /// condition can enable/hide the whole row; <paramref name="setHidden"/> lets the owning
    /// layout collapse the row instead of leaving a gap.
    /// </summary>
    public FieldSet AttachRow(string key, IEnumerable<Control> companions, Action<bool>? setHidden = null)
    {
        RowParts r = Row(key);
        r.Companions.AddRange(companions.Where(c => c is not null));
        r.SetHidden = setHidden;
        return this;
    }

    /// <summary>The field (and its row) is enabled only while <paramref name="condition"/> holds.</summary>
    public FieldSet EnableWhen(string key, Func<FieldSet, bool> condition)
    {
        Row(key).EnabledWhen = condition;
        Refresh();
        return this;
    }

    /// <summary>The field's row is shown only while <paramref name="condition"/> holds (hidden rows take no space).</summary>
    public FieldSet VisibleWhen(string key, Func<FieldSet, bool> condition)
    {
        Row(key).VisibleWhen = condition;
        Refresh();
        return this;
    }

    /// <summary>True unless a condition currently disables or hides the field.</summary>
    public bool IsActive(string key) => !_rows.TryGetValue(key, out var r) || (!r.Disabled && !r.Hidden);

    /// <summary>Raised after conditions have been re-evaluated and some row changed state.</summary>
    public event Action? ConditionsChanged;

    /// <summary>
    /// Re-evaluate every EnableWhen/VisibleWhen condition (done automatically whenever a value
    /// changes). A condition that names a field not registered yet is skipped for now.
    /// </summary>
    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        bool changed = false;
        try
        {
          // a condition may look at a field that is itself conditional: repeat until stable
          for (int pass = 0, before = -1; pass < 5; pass++)
          {
            int state = _rows.Values.Sum(r => (r.Disabled ? 1 : 0) + (r.Hidden ? 2 : 0));
            if (state == before) break;
            before = state;
            foreach (var (key, control) in _fields)
            {
                if (!_rows.TryGetValue(key, out RowParts? r)) continue;
                bool disabled = r.Disabled, hidden = r.Hidden;
                try
                {
                    if (r.EnabledWhen is not null) disabled = !r.EnabledWhen(this);
                    if (r.VisibleWhen is not null) hidden = !r.VisibleWhen(this);
                }
                catch (KeyNotFoundException) { continue; }   // refers to a field added later

                if (disabled != r.Disabled)
                {
                    r.Disabled = disabled;
                    control.Enabled = !disabled;
                    foreach (Control c in r.Companions)
                    {
                        // A disabled Label is drawn embossed grey (about 2.4:1, unreadable on the
                        // dark theme): keep it enabled and dim it to the theme's Subtle colour.
                        if (c is Label label) Dim(label, disabled);
                        else c.Enabled = !disabled;
                    }
                    changed = true;
                }
                if (hidden != r.Hidden)
                {
                    r.Hidden = hidden;
                    if (r.SetHidden is not null) r.SetHidden(hidden);
                    else { control.Visible = !hidden; foreach (Control c in r.Companions) c.Visible = !hidden; }
                    changed = true;
                }
                if (disabled || hidden) Mark(control, null);   // an inactive field can't be invalid
            }
          }
        }
        finally { _refreshing = false; }
        if (changed) ConditionsChanged?.Invoke();
    }

    private void HookValueChanged(Control c)
    {
        void Changed(object? s, EventArgs e) { if (_rows.Count > 0) Refresh(); }
        switch (c)
        {
            case CheckBox cb: cb.CheckedChanged += Changed; break;
            case NumericUpDown n: n.ValueChanged += Changed; break;
            case ComboBox combo: combo.SelectedIndexChanged += Changed; combo.TextChanged += Changed; break;
            default: c.TextChanged += Changed; break;
        }
    }

    /// <summary>Mark a field as required (must be non-empty).</summary>
    public FieldSet Require(string key) { _ = this[key]; _required.Add(key); return this; }

    /// <summary>Attach a rule: return an error message, or null if the value is fine.</summary>
    public FieldSet Rule(string key, Func<string, string?> rule)
    {
        _ = this[key];
        _rules[key] = rule;
        return this;
    }

    // --------------------------------------------------------------- values

    public string GetValue(string key) => ReadValue(this[key]);

    public void SetValue(string key, string value) => WriteValue(this[key], value);

    // ------------------------------------------------------- typed access

    /// <summary>The field's text, trimmed.</summary>
    public string Text(string key) => GetValue(key).Trim();

    /// <summary>A checkbox field (or "true"/"false" text) as bool.</summary>
    public bool Bool(string key)
        => bool.TryParse(GetValue(key).Trim(), out bool b) ? b : throw Bad(key, "true/false");

    /// <summary>A number field as decimal — independent of the current culture.</summary>
    public decimal Decimal(string key)
    {
        Control c = this[key];
        if (c is NumericUpDown n) return Math.Round(n.Value, n.DecimalPlaces, MidpointRounding.AwayFromZero);
        return AiFgfNumbers.TryParse(c.Text, out decimal d) ? d : throw Bad(key, "a number");
    }

    /// <summary>A number field as int (must be a whole number).</summary>
    public int Int(string key)
    {
        decimal d = Decimal(key);
        return d == Math.Truncate(d) && d >= int.MinValue && d <= int.MaxValue ? (int)d : throw Bad(key, "a whole number");
    }

    /// <summary>A choice field as an enum value (names match case-insensitively).</summary>
    public TEnum Get<TEnum>(string key) where TEnum : struct, Enum
        => Enum.TryParse(GetValue(key).Trim(), ignoreCase: true, out TEnum v) && Enum.IsDefined(v) ? v : throw Bad(key, $"one of {string.Join(", ", Enum.GetNames<TEnum>())}");

    private FormatException Bad(string key, string expected)
        => new($"Field '{key}' is '{GetValue(key)}', expected {expected}.");

    /// <summary>All field values as strings (checkboxes as "true"/"false", numbers invariant).</summary>
    public Dictionary<string, string> GetValues()
        => _fields.ToDictionary(f => f.Key, f => ReadValue(f.Control), StringComparer.OrdinalIgnoreCase);

    /// <summary>Values of the fields no condition currently disables or hides — what a run should use.</summary>
    public Dictionary<string, string> GetActiveValues()
        => _fields.Where(f => IsActive(f.Key)).ToDictionary(f => f.Key, f => ReadValue(f.Control), StringComparer.OrdinalIgnoreCase);

    /// <summary>Apply values by key; unknown keys are ignored so old settings files stay loadable.</summary>
    public void SetValues(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (key, control) in _fields)
            if (values.TryGetValue(key, out string? v) && v is not null)
                WriteValue(control, v);
        Refresh();
    }

    // ----------------------------------------------------------- validation

    /// <summary>
    /// Validate every field. Invalid fields are tinted and get a tooltip; the first one is
    /// focused. Returns the list of errors (empty when valid).
    /// </summary>
    public IReadOnlyList<FieldError> Validate()
    {
        var errors = new List<FieldError>();
        Refresh();
        foreach (var (key, control) in _fields)
        {
            if (!IsActive(key)) { Mark(control, null); continue; }   // disabled/hidden: not validated
            string value = ReadValue(control);
            string? message = null;
            if (_required.Contains(key) && string.IsNullOrWhiteSpace(value) && control is not CheckBox)
                message = $"{key} is required.";
            else if (_rules.TryGetValue(key, out var rule))
                message = rule(value);

            if (message is null) Mark(control, null);
            else { Mark(control, message); errors.Add(new FieldError(key, message)); }
        }
        if (errors.Count > 0) Find(errors[0].Key)?.Focus();
        return errors;
    }

    /// <summary>True if <paramref name="key"/> is currently marked invalid.</summary>
    public bool IsMarkedInvalid(string key) => _invalid.ContainsKey(this[key]);

    // ---------------------------------------------------------------- impl

    // dimmed label → its ForeColor before it was dimmed
    // (with the theme current then: after AiFgfTheme.Switch the colour is mapped to the new theme)
    private readonly Dictionary<Label, (Color Color, AiFgfTheme Theme)> _dimmed = new();

    private void Dim(Label label, bool dim)
    {
        if (dim) { _dimmed.TryAdd(label, (label.ForeColor, AiFgfTheme.Current)); label.ForeColor = AiFgfTheme.Current.Subtle; }
        else if (_dimmed.Remove(label, out var original)) label.ForeColor = AiFgfTheme.Remap(original.Color, original.Theme, AiFgfTheme.Current, foreground: true);
    }

    /// <summary>True while the row of <paramref name="key"/> is disabled by a condition (its label is dimmed, not disabled).</summary>
    public bool IsDisabled(string key) => _rows.TryGetValue(key, out var r) && r.Disabled;

    // invalid control → the BackColor it had before it was tinted (restored when it becomes valid)
    private readonly Dictionary<Control, (Color Color, AiFgfTheme Theme)> _invalid = new();

    private Control? Find(string key)
        => _fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).Control;

    private void Mark(Control c, string? message)
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

    private void HookClearOnEdit(Control c)
    {
        void Clear(object? s, EventArgs e) => Mark(c, null);
        switch (c)
        {
            case NumericUpDown n: n.ValueChanged += Clear; break;
            case CheckBox cb: cb.CheckedChanged += Clear; break;
            default: c.TextChanged += Clear; break; // TextBox, ComboBox, MaskedTextBox, DateTimePicker, custom controls…
        }
    }

    private static string ReadValue(Control c) => c switch
    {
        CheckBox cb => cb.Checked ? "true" : "false",
        NumericUpDown n => AiFgfNumbers.Format(n.Value, n.DecimalPlaces),   // as shown: "1.50", however the value was entered
        _ => c.Text,
    };

    private static void WriteValue(Control c, string value)
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
}
