using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// Fluent layout helpers that assemble the dock/table/flow structure the reference GUIs
/// built by hand: a top <b>parameters</b> section of labelled rows, a wrapping
/// <b>button bar</b>, and a <b>fill</b> body. Everything scales cleanly with DPI.
///
/// Dock order matters in WinForms (Fill must be added before Top/Bottom so it ends up
/// lowest in z-order); <see cref="FormScaffold"/> adds things in the right order for you.
/// </summary>
public sealed partial class FormScaffold
{
    private readonly Form _form;
    private Control? _body;
    private FlowLayoutPanel? _buttonBar;
    private GroupBox? _paramsBox;
    private ParamTable? _paramTable;
    private readonly FieldSet _fields = new();

    private FormScaffold(Form form) => _form = form;

    /// <summary>Begin scaffolding an existing form with the theme applied.</summary>
    public static FormScaffold For(Form form)
    {
        var t = AiFgfTheme.Current;
        form.SuspendLayout();
        form.Font = t.UiFont();
        form.BackColor = t.App;
        form.ForeColor = t.Text;
        form.Padding = new Padding(10, 10, 10, 0);
        // DPI scaling for a hand-coded form: scale from a 96-DPI baseline to the monitor.
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.StartPosition = FormStartPosition.CenterScreen;
        AiFgfNativeTheme.Attach(form);   // dark drop-downs and scroll bars in a dark theme
        return new FormScaffold(form);
    }

    public FormScaffold Title(string text) { _form.Text = text; return this; }

    public FormScaffold Size(int width, int height) { _form.Size = new Size(width, height); return this; }

    public FormScaffold MinSize(int width, int height) { _form.MinimumSize = new Size(width, height); return this; }

    /// <summary>
    /// Build a "Parameters" group of labelled rows. The callback receives a
    /// <see cref="RowBuilder"/> to add one input per line. Rows are responsive: labels sit
    /// beside their inputs while there is room and stack above them when the form is narrow.
    /// Every input is registered in <see cref="Fields"/>.
    /// </summary>
    public FormScaffold Parameters(string caption, Action<RowBuilder> build, int? height = null)
    {
        var t = AiFgfTheme.Current;
        _paramsBox = new GroupBox
        {
            Text = caption,
            Dock = DockStyle.Top,
            AutoSize = height is null,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = height ?? 0,
            Padding = new Padding(12, 6, 12, 10),
            ForeColor = t.Subtle,
            BackColor = t.App,
        };
        _paramTable = new ParamTable { Dock = DockStyle.Top };
        build(new RowBuilder(_paramTable, _fields));
        // The rows sit in a scrolling panel: when the window is too short for all of them (many
        // fields on a small or high-scale screen) the rows scroll instead of being cut off —
        // see AutoMinSize, which caps the parameters' height and the window's minimum height.
        var scroller = new ParamScroller();
        scroller.Controls.Add(_paramTable);
        _paramsBox.Controls.Add(scroller);
        return this;
    }

    /// <summary>The named inputs added through <see cref="Parameters"/>.</summary>
    public FieldSet Fields => _fields;

    /// <summary>The responsive parameters table, if <see cref="Parameters"/> was called.</summary>
    public ParamTable? ParamTable => _paramTable;

    /// <summary>
    /// Build the wrapping button bar. With <see cref="BarAlign.Left"/> (default) buttons flow
    /// from the left; with <see cref="BarAlign.Right"/> they hug the right edge (dialog style)
    /// and reflow there as the form resizes. Either way the bar wraps instead of clipping.
    /// </summary>
    public FormScaffold Buttons(Action<BarBuilder> build, BarAlign align = BarAlign.Left)
    {
        var t = AiFgfTheme.Current;
        _buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = align == BarAlign.Right ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            Padding = new Padding(6, 4, 6, 6),
            BackColor = t.App,
        };
        build(new BarBuilder(_buttonBar));
        return this;
    }

    /// <summary>Set the fill body (a log, a grid, a tab control, anything).</summary>
    public FormScaffold Body(Control control) { control.Dock = DockStyle.Fill; _body = control; return this; }

    /// <summary>The wrapping button bar built by <see cref="Buttons"/>, if any — for <see cref="AutoMinSize"/>.</summary>
    public FlowLayoutPanel? ButtonBar => _buttonBar;

    /// <summary>Finish: add the controls to the form in correct dock z-order and resume layout.</summary>
    public void Done()
    {
        if (_body is not null) _form.Controls.Add(_body);           // Fill first (lowest z)
        if (_buttonBar is not null) _form.Controls.Add(_buttonBar); // Top
        if (_paramsBox is not null) _form.Controls.Add(_paramsBox); // Top (topmost of the two)
        // Dock order puts the body first, but Tab should go top-down: parameters, buttons, body.
        if (_paramsBox is not null) _paramsBox.TabIndex = 0;
        if (_buttonBar is not null) _buttonBar.TabIndex = 1;
        if (_body is not null) _body.TabIndex = 2;
        _form.ResumeLayout(performLayout: true);
    }

    // ------------------------------------------------------------ row builder

    /// <summary>
    /// Adds labelled rows to the parameters table and registers each input in the form's
    /// <see cref="FieldSet"/> under <c>key</c> (default: the label without its trailing colon).
    /// </summary>
    public sealed class RowBuilder
    {
        private readonly ParamTable _table;
        private readonly FieldSet _fields;

        internal RowBuilder(ParamTable table, FieldSet fields) { _table = table; _fields = fields; }

        /// <summary>The field registry rows are added to (for extra rules: <c>rows.Fields.Rule(...)</c>).</summary>
        public FieldSet Fields => _fields;

        private void Place(string label, Control input, Control? trailing, bool required, string? key)
        {
            // AutoSize + anchoring (set by ParamTable per mode) rather than Dock=Fill: a
            // TableLayoutPanel measures non-AutoSize controls by their *specified* size (a Label's
            // default 100×23), which makes row heights unpredictable across fonts and DPI.
            Label lbl = MakeLabel(required ? label.TrimEnd() + " *" : label);
            _table.AddRow(lbl, input, trailing);
            string k = key ?? KeyFrom(label);
            input.Name = k;   // AutomationId: a stable handle for UI-driving tests (FlaUI etc.)
            _fields.Add(k, input, required);
            var companions = trailing is null ? new Control[] { lbl } : new Control[] { lbl, trailing };
            _fields.AttachRow(k, companions, hidden => _table.SetRowHidden(input, hidden));
            _lastKeys = new[] { k };
        }

        private static Label MakeLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = AiFgfTheme.Current.Text,
            Margin = new Padding(3, 3, 8, 3),
        };

        /// <summary>
        /// A labelled row holding any control (a slider, a swatch bar…) that is not a field: it is laid
        /// out, stacked and DPI-scaled like the others but not registered in <see cref="Fields"/>.
        /// A <see cref="FlowLayoutPanel"/> gets the wrapped height it needs.
        /// </summary>
        public RowBuilder Custom(string label, Control control, Control? trailing = null)
        {
            ArgumentNullException.ThrowIfNull(control);
            _table.AddRow(MakeLabel(label), control, trailing);
            _lastKeys = Array.Empty<string>();
            return this;
        }

        private string[] _lastKeys = Array.Empty<string>();

        /// <summary>
        /// Enable the row just added only while <paramref name="condition"/> holds, e.g.
        /// <c>rows.FileText("Report:", out _).EnableWhen(f => f.GetValue("WriteReport") == "true")</c>.
        /// A disabled field is not validated and not part of <see cref="FieldSetBase{TSelf, TControl}.GetActiveValues"/>.
        /// </summary>
        public RowBuilder EnableWhen(Func<FieldSet, bool> condition)
        {
            foreach (string k in _lastKeys) _fields.EnableWhen(k, condition);
            return this;
        }

        /// <summary>Show the row just added only while <paramref name="condition"/> holds; hidden rows take no space.</summary>
        public RowBuilder VisibleWhen(Func<FieldSet, bool> condition)
        {
            foreach (string k in _lastKeys) _fields.VisibleWhen(k, condition);
            return this;
        }

        /// <summary>
        /// The field key for a label: no trailing colon or required star, and no mnemonic
        /// ampersand (a doubled ampersand stays a literal one).
        /// </summary>
        private static string KeyFrom(string label)
        {
            var key = new System.Text.StringBuilder(label.Length);
            for (int i = 0; i < label.Length; i++)
            {
                if (label[i] != '&') { key.Append(label[i]); continue; }
                if (i + 1 < label.Length && label[i + 1] == '&') { key.Append('&'); i++; }   // "&&" -> '&'
            }
            return key.ToString().Trim().TrimEnd(':', '*').Trim();
        }

        /// <summary>A text row; out-returns the <see cref="TextBox"/> so the caller can read it later.</summary>
        public RowBuilder Text(string label, out TextBox box, string? placeholder = null, bool required = false, string? key = null)
        {
            box = AiFgf.Text(placeholder);
#if NET11_0_OR_GREATER
            AiFgfDropView.Accept(box, DropTarget.Text);   // .NET 11: drag and drop - a dropped path or text
#endif
            Place(label, box, null, required, key);
            return this;
        }

        /// <summary>A text row read back by key: <c>Fields.Text("Host")</c>.</summary>
        public RowBuilder Text(string label, string? placeholder = null, string? value = null, bool required = false, string? key = null)
        {
            Text(label, out TextBox box, placeholder, required, key);
            if (value is not null) box.Text = value;
            return this;
        }

        /// <summary>A password row: the text is masked (the system's bullet), read back by key like any text.</summary>
        public RowBuilder Password(string label, string? placeholder = null, bool required = false, string? key = null)
            => Password(label, out _, placeholder, required, key);

        /// <inheritdoc cref="Password(string, string?, bool, string?)"/>
        public RowBuilder Password(string label, out TextBox box, string? placeholder = null, bool required = false, string? key = null)
        {
            box = AiFgf.Text(placeholder);
            box.UseSystemPasswordChar = true;
            Place(label, box, null, required, key);
            return this;
        }

        /// <summary>
        /// A multi-line text row (<see cref="AiFgfMemo"/>): lines never wrap — long ones scroll
        /// horizontally — and <paramref name="lines"/> lines are visible at any DPI.
        /// </summary>
        public RowBuilder Memo(string label, out AiFgfMemo memo, int lines = 4, string? value = null, bool required = false, string? key = null,
                               int maxLines = 12, bool readOnly = false)
        {
            memo = new AiFgfMemo { VisibleLines = lines, MaxLines = Math.Max(lines, maxLines), ReadOnly = readOnly };
#if NET11_0_OR_GREATER
            AiFgfDropView.Accept(memo, DropTarget.Memo);   // .NET 11: drag and drop - each dropped path on a line of its own
#endif
            if (value is not null) memo.Text = value;
            Place(label, memo, null, required, key);
            return this;
        }

        /// <inheritdoc cref="Memo(string, out AiFgfMemo, int, string?, bool, string?, int, bool)"/>
        public RowBuilder Memo(string label, int lines = 4, string? value = null, bool required = false, string? key = null,
                               int maxLines = 12, bool readOnly = false)
            => Memo(label, out _, lines, value, required, key, maxLines, readOnly);

        /// <summary>A text row with a "Browse…" button for a file.</summary>
        public RowBuilder FileText(string label, out TextBox box, string filter = "All files|*.*", string? title = null, bool required = false, string? key = null)
        {
            box = AiFgf.Text();
#if NET11_0_OR_GREATER
            AiFgfDropView.Accept(box, DropTarget.File, filter);   // .NET 11: drag and drop - a file from Explorer, matching the filter
#endif
            Place(label, box, AiFgf.BrowseFileInto(box, filter, title), required, key);
            return this;
        }

        /// <summary>A text row with a "Browse…" button for a folder.</summary>
        public RowBuilder FolderText(string label, out TextBox box, string? description = null, bool required = false, string? key = null)
        {
            box = AiFgf.Text();
#if NET11_0_OR_GREATER
            AiFgfDropView.Accept(box, DropTarget.Folder);   // .NET 11: drag and drop - a folder, or the folder of a file
#endif
            Place(label, box, AiFgf.BrowseFolderInto(box, description), required, key);
            return this;
        }

        public RowBuilder Combo(string label, out ComboBox combo, bool editable = true, bool required = false, string? key = null)
        {
            combo = AiFgf.Combo(editable);
            Place(label, combo, null, required, key);
            return this;
        }

        /// <summary>
        /// A drop-down list of fixed choices with one selected — never left at "no selection",
        /// and a saved value that is no longer offered keeps the default.
        /// </summary>
        public RowBuilder Choice(string label, IEnumerable<string> items, string? selected = null, string? key = null)
        {
            var combo = AiFgf.Combo(editable: false);
            foreach (string item in items) combo.Items.Add(item);
            if (combo.Items.Count == 0) throw new ArgumentException("A choice needs at least one item.", nameof(items));
            int index = selected is null ? 0 : combo.FindStringExact(selected);
            combo.SelectedIndex = index >= 0 ? index : throw new ArgumentException($"'{selected}' is not one of the items.", nameof(selected));
            Place(label, combo, null, required: false, key);
            return this;
        }

        /// <summary>A choice of the names of <typeparamref name="TEnum"/>; read it with <c>Fields.Get&lt;TEnum&gt;(key)</c>.</summary>
        public RowBuilder Choice<TEnum>(string label, TEnum selected, string? key = null) where TEnum : struct, Enum
            => Choice(label, Enum.GetNames<TEnum>(), selected.ToString(), key);

        /// <summary>
        /// One of a few fixed choices, all visible as radio buttons (<see cref="AiFgfRadioGroup"/>; they wrap). Read back
        /// like <see cref="Choice"/>: the selected item; a saved value no longer offered keeps the default.
        /// </summary>
        public RowBuilder Radios(string label, IEnumerable<string> items, string? selected = null, string? key = null)
        {
            var group = new AiFgfRadioGroup(items);
            if (selected is not null)
            {
                int index = group.Items.ToList().IndexOf(selected);
                group.SelectedIndex = index >= 0 ? index : throw new ArgumentException($"'{selected}' is not one of the items.", nameof(selected));
            }
            Place(label, group, null, required: false, key);
            return this;
        }

        /// <summary>
        /// A whole number on a slider (<see cref="AiFgfSlider"/>), its value shown beside it with an optional
        /// <paramref name="unit"/> ("40 %"); read back with <c>Fields.Int(key)</c>.
        /// </summary>
        public RowBuilder Slider(string label, int min = 0, int max = 100, int value = 0, string? unit = null, string? key = null)
            => Slider(label, out _, min, max, value, unit, key);

        /// <inheritdoc cref="Slider(string, int, int, int, string?, string?)"/>
        public RowBuilder Slider(string label, out AiFgfSlider slider, int min = 0, int max = 100, int value = 0, string? unit = null, string? key = null)
        {
            if (max < min) throw new ArgumentException("max is below min.", nameof(max));
            var s = new AiFgfSlider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), LargeChange = Math.Max(1, (max - min) / 10) };
            string Shown(int v) => unit is null ? v.ToString(System.Globalization.CultureInfo.CurrentCulture) : $"{v} {unit}";
            var shown = new Label { AutoSize = true, UseMnemonic = false, ForeColor = AiFgfTheme.Current.Text, Margin = new Padding(6, 3, 3, 3), Text = Shown(s.Value) };
            // as wide as the widest value, so the slider does not jump while it moves
            void Fit() => shown.MinimumSize = new Size(new[] { min, max }.Max(v => TextRenderer.MeasureText(Shown(v), shown.Font).Width), 0);
            shown.FontChanged += (_, _) => Fit();
            shown.HandleCreated += (_, _) => Fit();
            s.ValueChanged += (_, _) => shown.Text = Shown(s.Value);
            Place(label, s, shown, required: false, key);
            slider = s;
            return this;
        }

        /// <summary>A numeric spinner row read back by key: <c>Fields.Int("Count")</c>.</summary>
        public RowBuilder Number(string label, decimal min = 0, decimal max = 100, decimal value = 0, int decimals = 0, string? key = null)
            => Number(label, out _, min, max, value, decimals, key);

        /// <summary>A numeric spinner row.</summary>
        public RowBuilder Number(string label, out NumericUpDown number, decimal min = 0, decimal max = 100, decimal value = 0, int decimals = 0, string? key = null)
        {
            number = AiFgf.Number(min, max, value, decimals);
            Place(label, number, null, required: false, key);
            return this;
        }

        /// <summary>A row of checkboxes (wrapped in a flow panel). Each is a field keyed by its text.</summary>
        public RowBuilder Checks(params CheckBox[] boxes)
            => Checks(boxes.Select(b => (KeyFrom(b.Text), b)).ToArray());

        /// <summary>
        /// A row of checkboxes with explicit field keys — use when the caption is long or may be
        /// reworded, so saved settings and presets keep working: <c>rows.Checks(("Resolve", chk))</c>.
        /// </summary>
        public RowBuilder Checks(params (string Key, CheckBox Box)[] boxes)
        {
            // Not AutoSize: ParamTable sets its height to exactly what the wrapped checkboxes need.
            var flow = new FlowLayoutPanel
            {
                AutoSize = false,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                WrapContents = true,
                Margin = new Padding(0, 2, 0, 2),
            };
            foreach (var (_, box) in boxes) flow.Controls.Add(box);
            _table.AddRow(null, flow, null);
            foreach (var (key, box) in boxes) box.Name = key;   // AutomationId
            foreach (var (key, box) in boxes) _fields.Add(key, box);
            _lastKeys = boxes.Select(b => b.Key).ToArray();
            return this;
        }
    }

    // ------------------------------------------------------------ bar builder

    /// <summary>Adds buttons to the button bar.</summary>
    public sealed class BarBuilder
    {
        private readonly FlowLayoutPanel _bar;
        internal BarBuilder(FlowLayoutPanel bar) => _bar = bar;

        public BarBuilder Primary(string text, EventHandler onClick, out Button button)
        {
            button = AiFgf.PrimaryButton(text, onClick);
            _bar.Controls.Add(button);
            return this;
        }

        public BarBuilder Button(string text, EventHandler onClick, out Button button)
        {
            button = AiFgf.Button(text, onClick);
            _bar.Controls.Add(button);
            return this;
        }

        /// <summary>Add a pre-made button (e.g. one you keep a field reference to already).</summary>
        public BarBuilder Add(Button button) { _bar.Controls.Add(button); return this; }
    }
}
