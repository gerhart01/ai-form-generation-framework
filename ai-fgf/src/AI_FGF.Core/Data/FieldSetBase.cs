namespace AI_FGF;

/// <summary>
/// The model of a form's named inputs, shared by the WinForms and the Avalonia <c>FieldSet</c>: keys, required fields
/// and rules, conditions (<see cref="EnableWhen"/>, <see cref="VisibleWhen"/>, re-evaluated until stable), values as
/// strings (checkboxes "true"/"false", numbers invariant), typed reads and validation. No UI types: a front end
/// derives from it and says only how to read, write, mark, focus, enable and hide its controls.
/// </summary>
/// <typeparam name="TSelf">The deriving set (so the fluent methods and conditions see it).</typeparam>
/// <typeparam name="TControl">The front end's control type.</typeparam>
public abstract class FieldSetBase<TSelf, TControl>
    where TSelf : FieldSetBase<TSelf, TControl>
    where TControl : class
{
    private sealed class RowState
    {
        public Func<TSelf, bool>? EnabledWhen, VisibleWhen;
        public bool Disabled, Hidden;
    }

    private readonly List<(string Key, TControl Control)> _fields = new();
    private readonly HashSet<string> _required = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string, string?>> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RowState> _rows = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshing;

    private TSelf Self => (TSelf)this;

    /// <summary>All keys, in the order the fields were added.</summary>
    public IReadOnlyList<string> Keys => _fields.Select(f => f.Key).ToList();

    /// <summary>The control registered under <paramref name="key"/>.</summary>
    public TControl this[string key] => Find(key) ?? throw new KeyNotFoundException($"No field '{key}'.");

    public bool Contains(string key) => Find(key) is not null;

    /// <summary>The registered controls with their keys, in order.</summary>
    protected IEnumerable<(string Key, TControl Control)> Fields => _fields;

    /// <summary>Register a control. Keys are case-insensitive and must be unique.</summary>
    public TSelf Add(string key, TControl control, bool required = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(control);
        if (Contains(key)) throw new ArgumentException($"Duplicate field key '{key}'.", nameof(key));
        _fields.Add((key, control));
        if (required) _required.Add(key);
        OnAdded(control);
        return Self;
    }

    // ---------------------------------------------------------- conditions

    private RowState Row(string key) { _ = this[key]; return _rows.TryGetValue(key, out var r) ? r : _rows[key] = new RowState(); }

    /// <summary>The field (and its row) is enabled only while <paramref name="condition"/> holds.</summary>
    public TSelf EnableWhen(string key, Func<TSelf, bool> condition)
    {
        Row(key).EnabledWhen = condition;
        Refresh();
        return Self;
    }

    /// <summary>The field's row is shown only while <paramref name="condition"/> holds (hidden rows take no space).</summary>
    public TSelf VisibleWhen(string key, Func<TSelf, bool> condition)
    {
        Row(key).VisibleWhen = condition;
        Refresh();
        return Self;
    }

    /// <summary>True unless a condition currently disables or hides the field.</summary>
    public bool IsActive(string key) => !_rows.TryGetValue(key, out var r) || (!r.Disabled && !r.Hidden);

    /// <summary>True while the row of <paramref name="key"/> is disabled by a condition (its label is dimmed, not disabled).</summary>
    public bool IsDisabled(string key) => _rows.TryGetValue(key, out var r) && r.Disabled;

    /// <summary>True when any field has a condition (a value change then re-evaluates them).</summary>
    protected bool HasConditions => _rows.Count > 0;

    /// <summary>Raised after conditions have been re-evaluated and some row changed state.</summary>
    public event Action? ConditionsChanged;

    /// <summary>
    /// Re-evaluate every EnableWhen/VisibleWhen condition (done automatically whenever a value changes). A condition
    /// may look at a field that is itself conditional: repeated until stable. A condition that names a field not
    /// registered yet is skipped for now.
    /// </summary>
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
                    if (!_rows.TryGetValue(key, out RowState? r)) continue;
                    bool disabled = r.Disabled, hidden = r.Hidden;
                    try
                    {
                        if (r.EnabledWhen is not null) disabled = !r.EnabledWhen(Self);
                        if (r.VisibleWhen is not null) hidden = !r.VisibleWhen(Self);
                    }
                    catch (KeyNotFoundException) { continue; }   // refers to a field added later

                    if (disabled != r.Disabled) { r.Disabled = disabled; ApplyDisabled(key, control, disabled); changed = true; }
                    if (hidden != r.Hidden) { r.Hidden = hidden; ApplyHidden(key, control, hidden); changed = true; }
                    if (disabled || hidden) Mark(control, null);   // an inactive field can't be invalid
                }
            }
        }
        finally { _refreshing = false; }
        if (changed) ConditionsChanged?.Invoke();
    }

    /// <summary>Mark a field as required (must be non-empty).</summary>
    public TSelf Require(string key) { _ = this[key]; _required.Add(key); return Self; }

    /// <summary>Attach a rule: return an error message, or null if the value is fine.</summary>
    public TSelf Rule(string key, Func<string, string?> rule)
    {
        _ = this[key];
        _rules[key] = rule;
        return Self;
    }

    // --------------------------------------------------------------- values

    public string GetValue(string key) => ReadValue(this[key]);

    public void SetValue(string key, string value) { WriteValue(this[key], value); Refresh(); }

    /// <summary>The field's text, trimmed.</summary>
    public string Text(string key) => GetValue(key).Trim();

    /// <summary>A checkbox field (or "true"/"false" text) as bool.</summary>
    public bool Bool(string key) => bool.TryParse(GetValue(key).Trim(), out bool b) ? b : throw Bad(key, "true/false");

    /// <summary>A number field as decimal — independent of the current culture; a spinner as it shows its value.</summary>
    public decimal Decimal(string key)
        => SpinnerValue(this[key]) ?? (AiFgfNumbers.TryParse(GetValue(key), out decimal d) ? d : throw Bad(key, "a number"));

    /// <summary>A number field as int (must be a whole number).</summary>
    public int Int(string key)
    {
        decimal d = Decimal(key);
        return d == Math.Truncate(d) && d >= int.MinValue && d <= int.MaxValue ? (int)d : throw Bad(key, "a whole number");
    }

    /// <summary>A choice field as an enum value (names match case-insensitively).</summary>
    public TEnum Get<TEnum>(string key) where TEnum : struct, Enum
        => Enum.TryParse(GetValue(key).Trim(), ignoreCase: true, out TEnum v) && Enum.IsDefined(v) ? v : throw Bad(key, $"one of {string.Join(", ", Enum.GetNames<TEnum>())}");

    private FormatException Bad(string key, string expected) => new($"Field '{key}' is '{GetValue(key)}', expected {expected}.");

    /// <summary>All field values as strings (checkboxes as "true"/"false", numbers invariant).</summary>
    public Dictionary<string, string> GetValues()
        => _fields.ToDictionary(f => f.Key, f => ReadValue(f.Control), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The values to keep on disk - window settings, presets: every field but a secret one (a masked password box). A
    /// password used to be written to the settings file and to presets in plain text.
    /// </summary>
    public Dictionary<string, string> GetPersistableValues()
        => _fields.Where(f => !IsSecret(f.Control)).ToDictionary(f => f.Key, f => ReadValue(f.Control), StringComparer.OrdinalIgnoreCase);

    /// <summary>True for a field whose value must never be stored (a password box: its input is masked).</summary>
    protected virtual bool IsSecret(TControl control) => false;

    /// <summary>Values of the fields no condition currently disables or hides — what a run should use.</summary>
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

    /// <summary>
    /// Validate every active field (inactive ones are neither validated nor marked). Invalid fields are marked, the
    /// first one is focused. Returns the errors (empty when valid).
    /// </summary>
    public IReadOnlyList<FieldError> Validate()
    {
        var errors = new List<FieldError>();
        Refresh();
        foreach (var (key, control) in _fields)
        {
            if (!IsActive(key)) { Mark(control, null); continue; }
            string value = ReadValue(control);
            string? message = null;
            if (_required.Contains(key) && string.IsNullOrWhiteSpace(value) && !IsCheckBox(control)) message = $"{key} is required.";
            else if (_rules.TryGetValue(key, out var rule)) message = rule(value);
            Mark(control, message);
            if (message is not null) errors.Add(new FieldError(key, message));
        }
        if (errors.Count > 0 && Find(errors[0].Key) is { } first) Focus(first);
        return errors;
    }

    /// <summary>True if <paramref name="key"/> is currently marked invalid.</summary>
    public bool IsMarkedInvalid(string key) => IsMarked(this[key]);

    private TControl? Find(string key) => _fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).Control;

    // ------------------------------------------------- what the front end does

    /// <summary>A control was registered: hook its value changes (clear its mark, re-evaluate conditions).</summary>
    protected abstract void OnAdded(TControl control);

    /// <summary>The control's value as a string (checkbox "true"/"false", a number invariant and as shown).</summary>
    protected abstract string ReadValue(TControl control);

    /// <summary>Set the control from a string; a value it cannot take (an unknown choice, not a number) keeps the current one.</summary>
    protected abstract void WriteValue(TControl control, string value);

    /// <summary>A spinner's value rounded as it shows it, or null when the control is not a spinner.</summary>
    protected abstract decimal? SpinnerValue(TControl control);

    /// <summary>True for a check box (never "empty", so never failing Required).</summary>
    protected abstract bool IsCheckBox(TControl control);

    /// <summary>Mark the control invalid with <paramref name="message"/>, or clear the mark (null).</summary>
    protected abstract void Mark(TControl control, string? message);

    protected abstract bool IsMarked(TControl control);

    protected abstract void Focus(TControl control);

    /// <summary>Enable or disable the control and the rest of its row (a label dimmed, not disabled).</summary>
    protected abstract void ApplyDisabled(string key, TControl control, bool disabled);

    /// <summary>Show or hide the control's row (hidden rows take no space).</summary>
    protected abstract void ApplyHidden(string key, TControl control, bool hidden);
}
