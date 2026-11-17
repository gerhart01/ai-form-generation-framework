using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace AI_FGF.Avalonia;

/// <summary>
/// Builds a form the way the WinForms <c>FormScaffold</c> does — same calls, same layout:
/// <code>
/// FormScaffold.For(this).Title("Tool").Size(900, 600)
///     .Parameters("Parameters", rows => rows.Text("Host:", required: true).Number("Port:", min: 1, max: 65535, value: 443))
///     .Buttons(bar => bar.Primary("&amp;Run", OnRun, out _).Button("&amp;Cancel", OnCancel, out _))
///     .Body(log)
///     .Done();
/// </code>
/// Parameters (responsive rows in a frame with the caption on its line; they scroll only when the
/// window is too short) on top, a wrapping button bar, the body filling the rest. Sizes are
/// device-independent, so the same at every DPI.
/// </summary>
public sealed class FormScaffold
{
    private readonly Window _form;
    private readonly FieldSet _fields = new();
    private readonly bool _direct3D;
    private Control? _paramsBox;
    private ScrollViewer? _paramsScroll;
    private ParamTable? _paramTable;
    private WrapPanel? _buttonBar;
    private Control? _body;

    private FormScaffold(Window form, bool direct3D) { _form = form; _direct3D = direct3D; }

    /// <summary>
    /// Begin scaffolding a window: UI font and size, theme background. <paramref name="direct3D"/>:
    /// render buttons and field focus glow with the real Direct3D 12 renderer (<see cref="AiFgfRenderers"/>)
    /// instead of the cross-platform drop shadow, when that add-on is referenced and available.
    /// </summary>
    public static FormScaffold For(Window form, bool direct3D = false)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.FontFamily = AvTheme.UiFont;
        form.FontSize = AvTheme.UiFontSize;
        AvTheme.Bind(form, TemplatedControl.BackgroundProperty, AvTheme.AppKey);
        form.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return new FormScaffold(form, direct3D);
    }

    public FormScaffold Title(string text) { _form.Title = text; return this; }

    public FormScaffold Size(int width, int height) { _form.Width = width; _form.Height = height; return this; }

    public FormScaffold MinSize(int width, int height) { _form.MinWidth = width; _form.MinHeight = height; return this; }

    /// <summary>The smallest height the body keeps when the parameters have to scroll (logical px).</summary>
    public double MinBodyHeight { get; set; } = 180;

    /// <summary>…and at least this many lines of the window's font, so a large font keeps a usable body.</summary>
    public double MinBodyEm { get; set; } = 14;

    private double BodyFloor => Math.Max(MinBodyHeight, MinBodyEm * _form.FontSize * 1.33);

    /// <summary>The parameters frame with labelled rows built by <paramref name="build"/>.</summary>
    public FormScaffold Parameters(string caption, Action<RowBuilder> build, int? height = null)
    {
        _paramTable = new ParamTable();
        build(new RowBuilder(_paramTable, _fields, _direct3D));
        var frame = AvTheme.Bind(new Border
        {
            BorderThickness = new Thickness(1),
            Padding = WinFormsMetrics.GroupPadding,
            Margin = new Thickness(0, 8, 0, 0),
            Child = _paramTable,
        }, Border.BorderBrushProperty, AvTheme.BorderKey);
        var box = new Grid();
        box.Children.Add(frame);
        if (!string.IsNullOrEmpty(caption))
        {
            // a WinForms GroupBox: the caption sits on the frame's top line
            var text = new TextBlock
            {
                Text = caption, Padding = new Thickness(3, 0), Margin = new Thickness(8, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Classes = { AiFgfSnapshot.GroupCaptionClass },
            };
            AvTheme.Bind(text, TextBlock.ForegroundProperty, AvTheme.SubtleKey);
            AvTheme.Bind(text, TextBlock.BackgroundProperty, AvTheme.AppKey);
            box.Children.Add(text);
            // the frame's top line and the first row follow the caption's font (a larger text scale pushed it onto the row)
            void Follow()
            {
                double k = text.FontSize / AvTheme.UiFontSize;
                Thickness p = WinFormsMetrics.GroupPadding;
                frame.Margin = new Thickness(0, 8 * k, 0, 0);
                frame.Padding = new Thickness(p.Left, p.Top * k, p.Right, p.Bottom);
            }
            text.PropertyChanged += (_, e) => { if (e.Property == TextBlock.FontSizeProperty) Follow(); };
            Follow();
        }
        if (height is int h) box.Height = h;
        _paramsScroll = new ScrollViewer { Content = box, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _paramsBox = _paramsScroll;
        return this;
    }

    public FieldSet Fields => _fields;

    public ParamTable? ParamTable => _paramTable;

    /// <summary>A wrapping button bar (left or right aligned); it wraps rather than clips.</summary>
    public FormScaffold Buttons(Action<BarBuilder> build, BarAlign align = BarAlign.Left)
    {
        _buttonBar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 4, 6, 6),
            HorizontalAlignment = align == BarAlign.Right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
        };
        build(new BarBuilder(_buttonBar, _direct3D));
        return this;
    }

    public FormScaffold Body(Control control) { _body = control; return this; }

    public WrapPanel? ButtonBar => _buttonBar;

    /// <summary>The root panel <see cref="Done"/> puts into the window.</summary>
    public Grid? Root { get; private set; }

    /// <summary>Finish: parameters, buttons, body top to bottom; minimum size guarded; conditions applied.</summary>
    public void Done()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = WinFormsMetrics.FormPadding };
        void Add(Control? c, int row) { if (c is null) return; Grid.SetRow(c, row); root.Children.Add(c); }
        Add(_paramsBox, 0);
        Add(_buttonBar, 1);
        Add(_body, 2);
        _form.Content = Root = root;
        _fields.Refresh();
        _form.SizeChanged += (_, _) => FitParams();
        _form.Opened += (_, _) => { Guard(); FitParams(); };
    }

    /// <summary>The parameters scroll only when the window is too short for them plus the body's minimum.</summary>
    private void FitParams()
    {
        if (_paramsScroll is null) return;
        double bar = _buttonBar?.Bounds.Height ?? 0;
        double extra = _form.Content is Control c && c != Root ? c.Bounds.Height - (Root?.Bounds.Height ?? 0) : 0;
        _paramsScroll.MaxHeight = Math.Max(110, _form.Bounds.Height - bar - BodyFloor - Math.Max(0, extra));
    }

    /// <summary>A minimum size at which nothing overlaps or is cut off: the narrowest the rows and the widest button allow.</summary>
    public void Guard()
    {
        double rows = _paramTable is null ? 0 : _paramTable.MinimumContentWidth + 2 * 12 + 2 + 20;
        double buttons = _buttonBar?.Children.Select(b => { b.Measure(global::Avalonia.Size.Infinity); return b.DesiredSize.Width; }).DefaultIfEmpty(0).Max() + 12 ?? 0;
        _form.MinWidth = Math.Max(_form.MinWidth, Math.Max(Math.Max(rows, buttons), 320));
        _form.MinHeight = Math.Max(_form.MinHeight, Math.Min(528, 110 + (_buttonBar?.Bounds.Height ?? 40) + BodyFloor));
    }

    // ------------------------------------------------------------ row builder

    /// <summary>Adds labelled rows and registers each input under its key (default: the label without "&amp;" and ':').</summary>
    public sealed class RowBuilder
    {
        private readonly ParamTable _table;
        private readonly FieldSet _fields;
        private readonly bool _direct3D;
        private string[] _lastKeys = Array.Empty<string>();

        internal RowBuilder(ParamTable table, FieldSet fields, bool direct3D) { _table = table; _fields = fields; _direct3D = direct3D; }

        public FieldSet Fields => _fields;

        private static Label MakeLabel(string text, Control? target) => new()
        {
            Content = AiFgf.WrappingCaption(text, TextAlignment.Left),   // wraps when ParamTable caps it (MaxLabelEm)
            Target = target,
            Padding = new Thickness(WinFormsMetrics.TextPad(AvTheme.UiFontSize), 0),   // a WinForms Label's width includes TextRenderer's padding
            Margin = WinFormsMetrics.LabelMargin,
            VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>
        /// A field eligible for the Direct3D focus glow (what the drop-shadow path also targets:
        /// TextBox/AiFgfMemo, ComboBox, NumericUpDown/AiFgfNumber) wrapped with it when Direct3D is active.
        /// </summary>
        private Control Glowed(Control input)
            => _direct3D && input is TextBox or ComboBox or NumericUpDown && AiFgfRenderers.Direct3D is { IsAvailable: true } d3
                ? d3.WrapField(input, 4) : input;

        private void Place(string label, Control input, Control? trailing, bool required, string? key)
        {
            Label lbl = MakeLabel(required ? label.TrimEnd() + " *" : label, input);
            Control visual = Glowed(input);
            _table.AddRow(lbl, visual, trailing);
            string k = key ?? KeyFrom(label);
            _fields.Add(k, input, required);
            // the field itself drives value/validation; the wrapper (if any) must hide/show with the row too
            var companions = new List<Control> { lbl };
            if (visual != input) companions.Add(visual);
            if (trailing is not null) companions.Add(trailing);
            _fields.AttachRow(k, companions);
            _lastKeys = new[] { k };
        }

        /// <summary>"&amp;Input file:" → "Input file".</summary>
        internal static string KeyFrom(string label)
        {
            var key = new System.Text.StringBuilder(label.Length);
            for (int i = 0; i < label.Length; i++)
            {
                if (label[i] != '&') { key.Append(label[i]); continue; }
                if (i + 1 < label.Length && label[i + 1] == '&') { key.Append('&'); i++; }
            }
            return key.ToString().Trim().TrimEnd(':', '*').Trim();
        }

        /// <summary>A labelled row with any control that is not a field (a slider, swatches…).</summary>
        public RowBuilder Custom(string label, Control control, Control? trailing = null)
        {
            // a text in the input column (a note) wraps at the room it has, as on WinForms: one line, it was cut off at the
            // window's edge
            if (control is TextBlock { TextWrapping: TextWrapping.NoWrap } note) note.TextWrapping = TextWrapping.Wrap;
            _table.AddRow(MakeLabel(label, control), control, trailing);
            _lastKeys = Array.Empty<string>();
            return this;
        }

        public RowBuilder EnableWhen(Func<FieldSet, bool> condition)
        {
            foreach (string k in _lastKeys) _fields.EnableWhen(k, condition);
            return this;
        }

        public RowBuilder VisibleWhen(Func<FieldSet, bool> condition)
        {
            foreach (string k in _lastKeys) _fields.VisibleWhen(k, condition);
            return this;
        }

        public RowBuilder Text(string label, out TextBox box, string? placeholder = null, bool required = false, string? key = null)
        {
            box = AiFgf.Text(placeholder);
#if NET11_0_OR_GREATER
            AiFgfDropView.Accept(box, DropTarget.Text);   // .NET 11: drag and drop - a dropped path or text
#endif
            Place(label, box, null, required, key);
            return this;
        }

        public RowBuilder Text(string label, string? placeholder = null, string? value = null, bool required = false, string? key = null)
        {
            Text(label, out TextBox box, placeholder, required, key);
            if (value is not null) box.Text = value;
            return this;
        }

        /// <summary>A password row: the text is masked, read back by key like any text.</summary>
        public RowBuilder Password(string label, string? placeholder = null, bool required = false, string? key = null)
            => Password(label, out _, placeholder, required, key);

        public RowBuilder Password(string label, out TextBox box, string? placeholder = null, bool required = false, string? key = null)
        {
            box = AiFgf.Text(placeholder);
            box.PasswordChar = '●';
            Place(label, box, null, required, key);
            return this;
        }

        public RowBuilder Memo(string label, out AiFgfMemo memo, int lines = 4, string? value = null, bool required = false, string? key = null,
                               int maxLines = 12, bool readOnly = false)
        {
            memo = new AiFgfMemo { VisibleLines = lines, IsReadOnly = readOnly };
#if NET11_0_OR_GREATER
            AiFgfDropView.Accept(memo, DropTarget.Memo);   // .NET 11: drag and drop - each dropped path on a line of its own
#endif
            memo.MaxLines = Math.Max(lines, maxLines);
            if (value is not null) memo.Text = value;
            Place(label, memo, null, required, key);
            return this;
        }

        public RowBuilder Memo(string label, int lines = 4, string? value = null, bool required = false, string? key = null,
                               int maxLines = 12, bool readOnly = false)
            => Memo(label, out _, lines, value, required, key, maxLines, readOnly);

        public RowBuilder FileText(string label, out TextBox box, string? filter = null, string? title = null, bool required = false, string? key = null)
        {
            box = AiFgf.Text();
#if NET11_0_OR_GREATER
            AiFgfDropView.Accept(box, DropTarget.File, filter);   // .NET 11: drag and drop - a file, matching the filter
#endif
            Place(label, box, AiFgf.BrowseFileInto(box, filter ?? AiFgfText.AllFilesFilter, title, _direct3D), required, key);
            return this;
        }

        public RowBuilder FolderText(string label, out TextBox box, string? description = null, bool required = false, string? key = null)
        {
            box = AiFgf.Text();
#if NET11_0_OR_GREATER
            AiFgfDropView.Accept(box, DropTarget.Folder);   // .NET 11: drag and drop - a folder, or the folder of a file
#endif
            Place(label, box, AiFgf.BrowseFolderInto(box, description, _direct3D), required, key);
            return this;
        }

        public RowBuilder Combo(string label, out ComboBox combo, bool editable = true, bool required = false, string? key = null)
        {
            combo = AiFgf.Combo(editable);
            Place(label, combo, null, required, key);
            return this;
        }

        /// <summary>A fixed list with one item selected; never "no selection"; an unknown saved value keeps the default.</summary>
        public RowBuilder Choice(string label, IEnumerable<string> items, string? selected = null, string? key = null)
        {
            var combo = AiFgf.Combo(editable: false);
            foreach (string item in items) combo.Items.Add(item);
            if (combo.Items.Count == 0) throw new ArgumentException("A choice needs at least one item.", nameof(items));
            int index = selected is null ? 0 : combo.Items.Cast<object>().ToList().FindIndex(i => (string)i == selected);
            combo.SelectedIndex = index >= 0 ? index : throw new ArgumentException($"'{selected}' is not one of the items.", nameof(selected));
            Place(label, combo, null, required: false, key);
            return this;
        }

        public RowBuilder Choice<TEnum>(string label, TEnum selected, string? key = null) where TEnum : struct, Enum
            => Choice(label, Enum.GetNames<TEnum>(), selected.ToString(), key);

        /// <summary>One of a few fixed choices, all visible as radio buttons (they wrap); read back like a choice.</summary>
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

        /// <summary>A whole number on a slider, its value shown beside it with an optional unit ("40 %").</summary>
        public RowBuilder Slider(string label, int min = 0, int max = 100, int value = 0, string? unit = null, string? key = null)
            => Slider(label, out _, min, max, value, unit, key);

        public RowBuilder Slider(string label, out AiFgfSlider slider, int min = 0, int max = 100, int value = 0, string? unit = null, string? key = null)
        {
            if (max < min) throw new ArgumentException("max is below min.", nameof(max));
            var s = new AiFgfSlider { Minimum = min, Maximum = max, LargeChange = Math.Max(1, (max - min) / 10) };
            s.IntValue = value;
            string Shown(int v) => unit is null ? v.ToString(System.Globalization.CultureInfo.CurrentCulture) : $"{v} {unit}";
            // as wide as the widest value, so the slider does not jump while it moves
            var shown = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 3, 0) };
            foreach (int v in new[] { min, max }) shown.Children.Add(new TextBlock { Text = Shown(v), Opacity = 0, IsHitTestVisible = false });
            var text = new TextBlock { Text = Shown(s.IntValue) };
            shown.Children.Add(text);
            s.ValueChanged += (_, _) => text.Text = Shown(s.IntValue);
            Place(label, s, shown, required: false, key);
            slider = s;
            return this;
        }

        public RowBuilder Number(string label, decimal min = 0, decimal max = 100, decimal value = 0, int decimals = 0, string? key = null)
            => Number(label, out _, min, max, value, decimals, key);

        public RowBuilder Number(string label, out NumericUpDown number, decimal min = 0, decimal max = 100, decimal value = 0, int decimals = 0, string? key = null)
        {
            number = AiFgf.Number(min, max, value, decimals);
            Place(label, number, null, required: false, key);
            return this;
        }

        /// <summary>A wrapping row of checkboxes, each a field keyed by its caption.</summary>
        public RowBuilder Checks(params CheckBox[] boxes)
            => Checks(boxes.Select(b => (KeyFrom(AiFgf.Caption(b.Content).Replace("__", "\u0001").Replace("_", "").Replace("\u0001", "_")), b)).ToArray());

        /// <summary>A wrapping row of checkboxes with explicit keys.</summary>
        public RowBuilder Checks(params (string Key, CheckBox Box)[] boxes)
            => Toggles(boxes.Select(b => (b.Key, (ToggleButton)b.Box)).ToArray());

#if NET11_0_OR_GREATER
        /// <summary>.NET 11: a wrapping row of check boxes and switches (<see cref="AiFgf.Toggle"/>) with explicit keys.</summary>
        public RowBuilder Checks(params (string Key, ToggleButton Box)[] boxes) => Toggles(boxes);
#endif

        internal RowBuilder Toggles((string Key, ToggleButton Box)[] boxes)
        {
            var flow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = WinFormsMetrics.ChecksRowMargin };
            foreach (var (_, box) in boxes) flow.Children.Add(box);
            _table.AddRow(null, flow, null);
            foreach (var (key, box) in boxes) _fields.Add(key, box);
            _lastKeys = boxes.Select(b => b.Key).ToArray();
            return this;
        }
    }

    // ------------------------------------------------------------ bar builder

    public sealed class BarBuilder
    {
        private readonly WrapPanel _bar;
        private readonly bool _direct3D;
        internal BarBuilder(WrapPanel bar, bool direct3D) { _bar = bar; _direct3D = direct3D; }

        public BarBuilder Primary(string text, EventHandler onClick, out Button button)
        {
            button = AiFgf.PrimaryButton(text, onClick, _direct3D);
            _bar.Children.Add(button);
            return this;
        }

        public BarBuilder Button(string text, EventHandler onClick, out Button button)
        {
            button = AiFgf.Button(text, onClick, _direct3D);
            _bar.Children.Add(button);
            return this;
        }

        public BarBuilder Add(Button button) { _bar.Children.Add(button); return this; }
    }
}
