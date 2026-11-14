using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AI_FGF.Avalonia;

/// <summary>
/// The named inputs of a form — the same API as the WinForms <c>FieldSet</c>: values by key
/// (strings; checkboxes "true"/"false"; numbers invariant), typed reads (<see cref="Text"/>,
/// <see cref="Int"/>, <see cref="Decimal"/>, <see cref="Bool"/>, <see cref="Get{TEnum}"/>),
/// required fields and rules, non-modal validation (invalid fields marked with an error tip, the
/// first focused), and conditions: <see cref="EnableWhen"/> (disabled, label dimmed — still
/// readable) and <see cref="VisibleWhen"/> (row hidden, takes no space). Inactive fields are
/// neither validated nor in <see cref="GetActiveValues"/>.
/// </summary>
public sealed class FieldSet
{
    private sealed class RowParts
    {
        public readonly List<Control> Companions = new();
        public Func<FieldSet, bool>? EnabledWhen, VisibleWhen;
        public bool Disabled, Hidden;
    }

    private readonly List<(string Key, Control Control)> _fields = new();
    private readonly Dictionary<string, RowParts> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _required = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string, string?>> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Control> _invalid = new();
    private bool _refreshing;

    public IReadOnlyList<string> Keys => _fields.Select(f => f.Key).ToList();

    public Control this[string key] => Find(key) ?? throw new KeyNotFoundException($"No field '{key}'.");

    public bool Contains(string key) => Find(key) is not null;

    /// <summary>Register an input under <paramref name="key"/>.</summary>
    public FieldSet Add(string key, Control control, bool required = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(control);
        if (Contains(key)) throw new ArgumentException($"Duplicate field key '{key}'.", nameof(key));
        _fields.Add((key, control));
        if (required) _required.Add(key);
        HookChanges(control);
        return this;
    }

    /// <summary>The label, trailing button… that go with the field (hidden/disabled with it).</summary>
    public FieldSet AttachRow(string key, IEnumerable<Control> companions)
    {
        _ = this[key];
        Row(key).Companions.AddRange(companions.Where(c => c is not null));
        return this;
    }

    private RowParts Row(string key) => _rows.TryGetValue(key, out var r) ? r : _rows[key] = new RowParts();

    public FieldSet EnableWhen(string key, Func<FieldSet, bool> condition)
    {
        _ = this[key];
        Row(key).EnabledWhen = condition;
        Refresh();
        return this;
    }

    public FieldSet VisibleWhen(string key, Func<FieldSet, bool> condition)
    {
        _ = this[key];
        Row(key).VisibleWhen = condition;
        Refresh();
        return this;
    }

    public bool IsActive(string key) => !_rows.TryGetValue(key, out var r) || (!r.Disabled && !r.Hidden);

    /// <summary>True while the row of <paramref name="key"/> is disabled by a condition (its label is dimmed, not disabled).</summary>
    public bool IsDisabled(string key) => _rows.TryGetValue(key, out var r) && r.Disabled;

    /// <summary>Raised after a condition switched a row on or off.</summary>
    public event Action? ConditionsChanged;

    /// <summary>Re-evaluate every condition (done automatically whenever a value changes).</summary>
    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        bool changed = false;
        try
        {
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
                        control.IsEnabled = !disabled;
                        foreach (Control c in r.Companions)
                        {
                            // a label is dimmed, not disabled: disabled text is hard to read
                            if (c is Label or TextBlock) Dim(c, disabled);
                            else c.IsEnabled = !disabled;
                        }
                        changed = true;
                    }
                    if (hidden != r.Hidden)
                    {
                        r.Hidden = hidden;
                        control.IsVisible = !hidden;
                        foreach (Control c in r.Companions) c.IsVisible = !hidden;
                        changed = true;
                    }
                    if (disabled || hidden) Mark(control, null);
                }
            }
        }
        finally { _refreshing = false; }
        if (changed) ConditionsChanged?.Invoke();
    }

    private static void Dim(Control label, bool dim)
    {
        var property = label is TemplatedControl ? TemplatedControl.ForegroundProperty : TextBlock.ForegroundProperty;
        if (dim) AvTheme.Bind(label, property, AvTheme.SubtleKey);
        else label.ClearValue(property);   // (not null: a null Foreground means no brush — invisible text)
    }

    public FieldSet Require(string key) { _ = this[key]; _required.Add(key); return this; }

    /// <summary>A rule returns an error message, or null when the value is fine.</summary>
    public FieldSet Rule(string key, Func<string, string?> rule)
    {
        _ = this[key];
        _rules[key] = rule;
        return this;
    }

    public string GetValue(string key) => ReadValue(this[key]);

    public void SetValue(string key, string value) { WriteValue(this[key], value); Refresh(); }

    // ------------------------------------------------------- typed access

    public string Text(string key) => GetValue(key).Trim();

    public bool Bool(string key) => bool.TryParse(GetValue(key).Trim(), out bool b) ? b : throw Bad(key, "true/false");

    public decimal Decimal(string key)
    {
        Control c = this[key];
        if (c is AiFgfNumber f) return Math.Round(f.Value ?? f.Minimum, f.DecimalPlaces, MidpointRounding.AwayFromZero);
        if (c is NumericUpDown n) return n.Value ?? n.Minimum;
        return AiFgfNumbers.TryParse(GetValue(key), out decimal d) ? d : throw Bad(key, "a number");
    }

    public int Int(string key)
    {
        decimal d = Decimal(key);
        return d == Math.Truncate(d) && d >= int.MinValue && d <= int.MaxValue ? (int)d : throw Bad(key, "a whole number");
    }

    public TEnum Get<TEnum>(string key) where TEnum : struct, Enum
        => Enum.TryParse(GetValue(key).Trim(), ignoreCase: true, out TEnum v) && Enum.IsDefined(v) ? v : throw Bad(key, $"one of {string.Join(", ", Enum.GetNames<TEnum>())}");

    private FormatException Bad(string key, string expected) => new($"Field '{key}' is '{GetValue(key)}', expected {expected}.");

    public Dictionary<string, string> GetValues()
        => _fields.ToDictionary(f => f.Key, f => ReadValue(f.Control), StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> GetActiveValues()
        => _fields.Where(f => IsActive(f.Key)).ToDictionary(f => f.Key, f => ReadValue(f.Control), StringComparer.OrdinalIgnoreCase);

    /// <summary>Apply values by key; unknown keys are ignored so old settings files stay loadable.</summary>
    public void SetValues(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (key, control) in _fields)
            if (values.TryGetValue(key, out string? v) && v is not null) WriteValue(control, v);
        Refresh();
    }

    // ----------------------------------------------------------- validation

    public IReadOnlyList<FieldError> Validate()
    {
        var errors = new List<FieldError>();
        Refresh();
        foreach (var (key, control) in _fields)
        {
            if (!IsActive(key)) { Mark(control, null); continue; }
            string value = ReadValue(control);
            string? message = null;
            if (_required.Contains(key) && string.IsNullOrWhiteSpace(value) && control is not CheckBox) message = $"{key} is required.";
            else if (_rules.TryGetValue(key, out var rule)) message = rule(value);
            Mark(control, message);
            if (message is not null) errors.Add(new FieldError(key, message));
        }
        if (errors.Count > 0) Find(errors[0].Key)?.Focus();
        return errors;
    }

    public bool IsMarkedInvalid(string key) => _invalid.Contains(this[key]);

    private void Mark(Control c, string? message)
    {
        if (message is null)
        {
            if (_invalid.Remove(c)) { DataValidationErrors.ClearErrors(c); ToolTip.SetTip(c, null); c.ClearValue(TemplatedControl.BackgroundProperty); }
            return;
        }
        _invalid.Add(c);
        if (c is TemplatedControl tc and not CheckBox) tc.Background = AvTheme.Brush(AiFgfTheme.Current.Invalid);   // the WinForms tint
        DataValidationErrors.SetErrors(c, new object[] { message });
        ToolTip.SetTip(c, message);
    }

    // ---------------------------------------------------------------- impl

    private Control? Find(string key) => _fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).Control;

    private void HookChanges(Control c)
    {
        void Changed() { Mark(c, null); Refresh(); }
        switch (c)
        {
            case CheckBox cb: cb.IsCheckedChanged += (_, _) => Changed(); break;
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

    private static string ReadValue(Control c) => c switch
    {
        CheckBox cb => cb.IsChecked == true ? "true" : "false",
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

    private static void WriteValue(Control c, string value)
    {
        switch (c)
        {
            case CheckBox cb:
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
}
