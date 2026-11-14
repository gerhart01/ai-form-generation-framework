using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// The labelled-rows table behind <see cref="FormScaffold.Parameters"/>, made responsive:
/// side by side (label | input | trailing button) while there is room, and stacked
/// (label above, input + trailing button below) once the input would get narrower than
/// <see cref="MinInputEm"/> em. The switch uses hysteresis so a window resized right at
/// the breakpoint doesn't flicker between the two layouts.
///
/// Controls are added once and only re-positioned (<see cref="TableLayoutPanel.SetCellPosition"/>)
/// when the mode changes, so focus, values and event handlers are never disturbed.
/// </summary>
public sealed class ParamTable : TableLayoutPanel
{
    private sealed record Row(Label? Label, Control Input, Control? Trailing)
    {
        public bool Hidden { get; set; }
        /// <summary>
        /// The control actually placed in the grid cell for this row: <see cref="Input"/> itself,
        /// unless something (e.g. <see cref="AiFgfFieldGlow.Wrap"/>) has since retrofitted it inside a
        /// wrapper and told us via <see cref="AdoptVisual"/> - <see cref="Input"/> stays the stable
        /// identity <see cref="SetRowHidden"/>, field lookup and the combo-height fix key off.
        /// </summary>
        public Control Visual { get; set; } = Input;
    }

    private IEnumerable<Row> Shown => _rows.Where(r => !r.Hidden);

    private readonly List<Row> _rows = new();
    private bool _stacked;
    private bool _relayouting;

    public ParamTable()
    {
        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ApplyMode(stacked: false);
    }

    /// <summary>Stack when the input column would be narrower than this many em (font heights).</summary>
    [DefaultValue(16)]
    public int MinInputEm { get; set; } = 16;

    /// <summary>The narrowest an input may get in stacked mode, in em.</summary>
    [DefaultValue(8)]
    public int MinStackedInputEm { get; set; } = 8;

    /// <summary>
    /// Labels wider than this many em wrap onto more lines instead of widening the label column
    /// (a 300-character label made the window's minimum 3000 px wide).
    /// </summary>
    [DefaultValue(22)]
    public int MaxLabelEm { get; set; } = 22;

    /// <summary>A button beside a field (Browseâ€¦) wraps its caption beyond this many em.</summary>
    public const int MaxTrailingEm = 12;

    private void CapLabels()
    {
        var max = new Size(MaxLabelEm * Em, 0);
        foreach (Row r in _rows)
            if (r.Label is { } l && l.MaximumSize != max) l.MaximumSize = max;
    }

    /// <summary>True while labels are stacked above their inputs.</summary>
    public bool IsStacked => _stacked;

    internal void AddRow(Label? label, Control input, Control? trailing)
    {
        _rows.Add(new Row(label, input, trailing));
        // A caption as the input (a note) or beside it (a slider's value) sits on the row's centre line like the row
        // label â€” AiFgf.Label's flow-bar margins put it 3 px high; a trailing button stays centred beside a row a
        // wrapped label made taller (it was top-aligned while its text box was centred).
        foreach (Control? c in new[] { input, trailing })
            if (c is Label l) { l.Anchor = AnchorStyles.Left; l.Margin = new Padding(l.Margin.Left, 3, l.Margin.Right, 3); }
        if (trailing is not null and not Label) trailing.Anchor = AnchorStyles.Left;
        // a button beside a field wraps its caption sooner: one 25 em wide made the window's minimum wider than the screen
        if (trailing is AiFgfButton b && b.MaxTextEm > MaxTrailingEm) b.MaxTextEm = MaxTrailingEm;
        SuspendLayout();   // adding a control lays the table out; with many rows that was O(rowsÂ²)
        if (label is not null) { label.MaximumSize = new Size(MaxLabelEm * Em, 0); Controls.Add(label); }
        Controls.Add(input);
        if (trailing is not null) Controls.Add(trailing);
        ApplyMode(_stacked, layoutNow: false);
        ResumeLayout(performLayout: false);   // laid out once, when the form next lays out
    }

    /// <summary>
    /// Tell the table that <paramref name="wrapper"/> now sits where <paramref name="original"/> used to
    /// (e.g. <see cref="AiFgfFieldGlow.Wrap"/> retrofitted a glow halo onto a row's field): the grid cell
    /// <paramref name="original"/> already held is handed to <paramref name="wrapper"/> right away, and
    /// every later layout pass (a resize crossing the stacked/unstacked breakpoint, say) places
    /// <paramref name="wrapper"/> there too. <paramref name="original"/> stays the row's identity for
    /// <see cref="SetRowHidden"/>, <see cref="IsRowHidden"/> and <see cref="FieldSet"/> lookups.
    /// </summary>
    internal void AdoptVisual(Control original, Control wrapper)
    {
        Row? row = _rows.FirstOrDefault(r => r.Input == original);
        if (row is null) return;
        row.Visual = wrapper;
        SetCellPosition(wrapper, GetCellPosition(original));
        SetColumnSpan(wrapper, GetColumnSpan(original));
        SetRowSpan(wrapper, GetRowSpan(original));
    }

    private readonly Dictionary<ComboBox, int> _comboHeights = new();

    // ------------------------------------------------------------ metrics

    private int Em => Math.Max(1, Font.Height);
    private int LabelWidth => Shown.Where(r => r.Label is not null).Select(r => Pref(r.Label!).Width + r.Label!.Margin.Horizontal).DefaultIfEmpty(0).Max();
    private int TrailingWidth => Shown.Where(r => r.Trailing is not null).Select(r => Pref(r.Trailing!).Width + r.Trailing!.Margin.Horizontal).DefaultIfEmpty(0).Max();

    /// <summary>Below this width the rows stack (label above input).</summary>
    public int StackBreakpoint => LabelWidth + MinInputEm * Em + TrailingWidth + Padding.Horizontal;

    /// <summary>The narrowest width at which the stacked layout still shows everything.</summary>
    public int MinimumContentWidth
    {
        get
        {
            int inputs = MinStackedInputEm * Em + TrailingWidth;
            int widestFlow = Shown.Where(r => r.Visual is FlowLayoutPanel)
                .Select(r => WidestChild((FlowLayoutPanel)r.Visual)).DefaultIfEmpty(0).Max();
            return Math.Max(LabelWidth, Math.Max(inputs, widestFlow)) + Padding.Horizontal;
        }
    }

    /// <summary>The height these rows need at <paramref name="width"/>, in the mode that width selects.</summary>
    public int HeightAt(int width)
    {
        // the mode the table takes when resized to that width â€” with the hysteresis: a stacked table stays stacked
        // up to an em past the breakpoint, and predicting side by side there gave half its height (the body was squeezed)
        bool stacked = WantStacked(width);
        int labelW = stacked ? 0 : LabelWidth;
        int h = Padding.Vertical;
        foreach (Row r in Shown)
        {
            int inputW = Math.Max(1, width - Padding.Horizontal - labelW - (r.Trailing is null ? 0 : TrailingWidth));
            int inputH = r.Visual switch
            {
                FlowLayoutPanel flow => WrappedHeight(flow, inputW - flow.Margin.Horizontal) + flow.Margin.Vertical,
                Label note => NoteHeight(note, inputW - note.Margin.Horizontal) + note.Margin.Vertical,
                _ => Measured(r.Visual),
            };
            int trailH = r.Trailing is null ? 0 : Measured(r.Trailing);
            int labelH = r.Label is null ? 0 : Measured(r.Label);
            int line = Math.Max(inputH, trailH);
            h += stacked ? labelH + line : Math.Max(labelH, line);
        }
        return h;
    }

    // ------------------------------------------------------------- layout

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_relayouting || _rows.Count == 0) return;
        // Only a *width* change can change the mode. Height changes on their own (e.g. the rows
        // growing right after a font change, before the new width arrives) must not trigger a
        // decision against a stale width.
        if (Width == _lastWidth) return;
        _lastWidth = Width;
        // A container mid-layout can hand the table a transient width of 0 or a few pixels;
        // deciding on it stacks the rows, which re-lays the container out at the real width,
        // which un-stacks them â€” for ever (found by the 60-row stress form). Keep the mode.
        if (!IsRealWidth(Width)) return;
        // Before the window exists the width is a placeholder: a new control is 200 px wide, and docking it into a
        // form that is not laid out yet passes that on. Deciding on it stacked every table while the form was being
        // built, and the real width un-stacked it again - two full rebuilds of every row each time a form opened.
        // Decide once the window exists (ResumeCreated).
        if (!IsHandleCreated) { _modeDirty = true; return; }
        _modeDirty = false;
        bool want = WantStacked(Width);
        if (want != _stacked) ApplyMode(want);
    }

    /// <summary>
    /// The mode for <paramref name="width"/>: stack below the breakpoint; once stacked, only
    /// un-stack a full em above it, so a window resized right at the breakpoint doesn't flicker.
    /// </summary>
    private bool WantStacked(int width) => _stacked ? width < StackBreakpoint + Em : width < StackBreakpoint;

    /// <summary>A width the table can really be laid out at (not a transient 0 from a container mid-layout).</summary>
    private bool IsRealWidth(int width) => width >= Em * 4;

    private bool _modeDirty;
    private int _lastWidth = -1;

    private void ReevaluateIfDirty()
    {
        if (!_modeDirty || _relayouting || IsDisposed || !IsRealWidth(Width)) return;
        _modeDirty = false;
        bool want = WantStacked(Width);
        if (want != _stacked) ApplyMode(want);
    }

    /// <summary>How many times the layout mode has switched (diagnostics/tests).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ModeSwitches { get; private set; }

    private bool _fitting;

    /// <summary>
    /// Size wrapping rows (e.g. a row of checkboxes) to exactly the height their contents
    /// need at their current width. An AutoSize FlowLayoutPanel inside a TableLayoutPanel
    /// cell is measured against an unreliable width constraint, so its height is set here
    /// with the same function <see cref="HeightAt"/> uses â€” layout and prediction agree.
    /// </summary>
    private bool _creatingChildren;

    // Creating each child's window (text boxes, spinners, memosâ€¦) re-lays the table out: 60 rows
    // meant 60 passes while the form was shown. Hold the layout while the children are created and
    // lay out once when they all exist (OnCreateControl runs right after â€” synchronously, so code
    // that lays the form out without pumping messages never finds the table suspended).
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_creatingChildren || Created) return;   // first creation only: a recreated handle gets no OnCreateControl
        _creatingChildren = true;
        SuspendLayout();
        // safety net: should OnCreateControl not come (a handle forced without CreateControl), resume anyway
        var sync = System.Threading.SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();
        sync.Post(_ => ResumeCreated(), null);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        ResumeCreated();
    }

    private void ResumeCreated()
    {
        if (!_creatingChildren) return;
        _creatingChildren = false;
        if (IsDisposed) return;
        ResumeLayout(performLayout: true);
        _modeDirty = true;   // the mode was left alone while the width was a placeholder: decide it at the real one
        ReevaluateIfDirty();
    }

    /// <summary>How many layout passes this table has run (a diagnostic: a pass is O(rows)).</summary>
    public int LayoutPasses { get; private set; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        LayoutPasses++;
        // A table sizes a row from a fixed-size control's *specified* height, and a combo grows by itself after a
        // font or DPI change without changing that: its row stayed low and the drop-down covered the next row.
        foreach (Row r in _rows)
            foreach (Control? c in new[] { r.Input, r.Trailing })
                if (c is ComboBox combo && (!_comboHeights.TryGetValue(combo, out int set) || set != combo.PreferredHeight))
                {
                    _comboHeights[combo] = combo.PreferredHeight;   // once per change: the combo may round it a pixel down
                    combo.Height = combo.PreferredHeight;
                }
        // A row holding a ComboBox or an AiFgfMemo cannot stay an AutoSize row: TableLayoutPanel measures
        // an AutoSize row's height once and does not reliably re-measure it from a later, taller height
        // such a child takes on by itself (a ComboBox after a font/DPI change, above; an AiFgfMemo after
        // longer text) - confirmed experimentally, growing the row back up after it had shrunk failed
        // even via an explicit PerformLayout(child, "Bounds"). Giving the row an explicit Absolute
        // height, recomputed every pass from the child's own (already-correct) height, sidesteps that
        // AutoSize engine entirely for just that row - without it, the label beside it centred on the
        // row's stale height instead of the child's real one, and the next row (in another container,
        // e.g. a checkbox FlowLayoutPanel) started before the child actually ended.
        foreach (Row r in _rows)
            // (also when the field sits in a wrapper such as AiFgfFieldGlow: the wrapper follows the field's height)
            if (r.Input is ComboBox or AiFgfMemo && GetPositionFromControl(r.Visual) is { Row: >= 0 } at && at.Row < RowStyles.Count)
            {
                int h = Measured(r.Visual);
                if (r.Trailing is not null) h = Math.Max(h, Measured(r.Trailing));
                if (!_stacked && r.Label is not null) h = Math.Max(h, Measured(r.Label));
                if (RowStyles[at.Row].SizeType != SizeType.Absolute || (int)RowStyles[at.Row].Height != h)
                    RowStyles[at.Row] = new RowStyle(SizeType.Absolute, h);
            }
        // A label beside a wrapping row (radio buttons) lines up with its first line: centred on all of them it sat
        // between two lines, and on one line the radio buttons' top margin put it 3 px high.
        foreach (Row r in _rows)
        {
            if (r.Label is not { } l) continue;
            int pad = 0;
            AnchorStyles anchor = _stacked ? AnchorStyles.Left | AnchorStyles.Bottom : AnchorStyles.Left;
            if (!_stacked && r.Input is FlowLayoutPanel flow && flow.Controls.Cast<Control>().FirstOrDefault(c => c.Visible) is { } first)
            {
                int text = Pref(l).Height - l.Padding.Vertical;
                int line = first.AutoSize ? Pref(first).Height : first.Height;
                pad = Math.Max(0, flow.Margin.Top + flow.Padding.Top + first.Margin.Top + (line - text) / 2 - l.Margin.Top);
                anchor = AnchorStyles.Left | AnchorStyles.Top;
            }
            if (l.Anchor != anchor) l.Anchor = anchor;
            if (l.Padding.Top != pad) l.Padding = new Padding(0, pad, 0, 0);
        }
        // A note wraps at its column (as the last pass laid it out) instead of widening the table past the window.
        // Set before the table lays out: set after, inside its pass, the label came out 0 px high.
        if (_rows.Any(r => r.Input is Label) && GetColumnWidths() is { Length: > 0 } columns)
            foreach (Row r in _rows)
                if (r.Input is Label note && GetPositionFromControl(note) is { Column: >= 0 } at && at.Column < columns.Length)
                {
                    int w = Math.Max(Em, columns.Skip(at.Column).Take(GetColumnSpan(note)).Sum() - note.Margin.Horizontal);
                    if (note.MaximumSize.Width != w) note.MaximumSize = new Size(w, 0);
                }
        base.OnLayout(levent);
        if (_fitting) return;
        _fitting = true;
        try
        {
            foreach (Row r in _rows)
                if (r.Input is FlowLayoutPanel flow && flow.Width > 0)
                {
                    int h = WrappedHeight(flow, flow.Width);
                    if (flow.Height != h) flow.Height = h;
                }
            // The same AutoSize-engine staleness as the row-height fix above, one step further: after a
            // real (not simulated) per-monitor DPI rescale, TableLayoutPanel's own vertical cell-alignment
            // for a ComboBox or AiFgfMemo can drift below where the row (now correctly, freshly sized) actually
            // puts it - confirmed experimentally, reproducing the exact pixel values of a real failure (B04).
            // Position it ourselves from the row heights we just asked for, the same way the fix above owns
            // the row's height instead of trusting the engine to keep it current.
            int[] rowTops = GetRowHeights();
            foreach (Row r in _rows)
                if (r.Input is ComboBox or AiFgfMemo && GetPositionFromControl(r.Visual) is { Row: >= 0 } at)
                {
                    int top = Padding.Top + r.Visual.Margin.Top;
                    for (int i = 0; i < at.Row && i < rowTops.Length; i++) top += rowTops[i];
                    if (r.Visual.Top != top) r.Visual.Top = top;
                }
        }
        finally { _fitting = false; }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        // On a DPI change the new font can arrive while the width is still the old one, and the
        // font change itself lays the table out â€” deciding then would flip modes twice (and skip
        // hysteresis). Let the next resize decide, or, if none comes, decide once the current
        // message (the whole rescale) has finished.
        if (_rows.Count == 0) return;
        CapLabels();   // the cap is in em: follow the font
        _modeDirty = true;
        if (IsHandleCreated) BeginInvoke(ReevaluateIfDirty);
    }

    /// <summary>Raised after a row has been hidden or shown (the table's height changed).</summary>
    public event EventHandler? RowsChanged;

    /// <summary>True if the row whose input is <paramref name="input"/> is hidden.</summary>
    public bool IsRowHidden(Control input) => _rows.FirstOrDefault(r => r.Input == input)?.Hidden ?? false;

    /// <summary>
    /// Hide or show a whole row (label, input, trailing button). A hidden row takes no space
    /// and is left out of every measurement (height prediction, column widths, breakpoint).
    /// </summary>
    public void SetRowHidden(Control input, bool hidden)
    {
        Row? row = _rows.FirstOrDefault(r => r.Input == input);
        if (row is null || row.Hidden == hidden) return;
        row.Hidden = hidden;
        SuspendLayout();
        if (row.Label is not null) row.Label.Visible = !hidden;
        row.Visual.Visible = !hidden;
        if (row.Trailing is not null) row.Trailing.Visible = !hidden;
        ResumeLayout(performLayout: true);
        // the label/trailing columns may have changed width, so the breakpoint may have moved
        if (!IsHandleCreated) _modeDirty = true;   // decided once the window exists (see OnSizeChanged)
        else if (IsRealWidth(Width) && !_relayouting) { bool want = WantStacked(Width); if (want != _stacked) ApplyMode(want); }
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyMode(bool stacked, bool layoutNow = true)
    {
        _relayouting = true;
        SuspendLayout();
        try
        {
            ColumnStyles.Clear();
            RowStyles.Clear();
            if (!stacked)
            {
                ColumnCount = 3;
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // labels
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));      // inputs
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // Browseâ€¦ etc.
                int r = 0;
                foreach (Row row in _rows)
                {
                    RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    if (row.Label is not null) { Place(row.Label, 0, r, 1); row.Label.Anchor = AnchorStyles.Left; } // vertically centred
                    Place(row.Visual, 1, r, row.Trailing is null ? 2 : 1);
                    if (row.Trailing is not null) Place(row.Trailing, 2, r, 1);
                    r++;
                }
                RowCount = r;
            }
            else
            {
                ColumnCount = 2;
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));      // label / input
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // Browseâ€¦ etc.
                int r = 0;
                foreach (Row row in _rows)
                {
                    if (row.Label is not null)
                    {
                        RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        Place(row.Label, 0, r, 2);
                        row.Label.Anchor = AnchorStyles.Left | AnchorStyles.Bottom; // sits just above its input
                        r++;
                    }
                    RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    Place(row.Visual, 0, r, row.Trailing is null ? 2 : 1);
                    if (row.Trailing is not null) Place(row.Trailing, 1, r, 1);
                    r++;
                }
                RowCount = r;
            }
            if (_stacked != stacked) ModeSwitches++;
            _stacked = stacked;
        }
        finally
        {
            ResumeLayout(performLayout: layoutNow);
            _relayouting = false;
        }
    }

    private void Place(Control c, int col, int row, int span)
    {
        SetCellPosition(c, new TableLayoutPanelCellPosition(col, row));
        SetColumnSpan(c, span);
    }

    // ------------------------------------------------------------ helpers

    private static Size Pref(Control c) => c.GetPreferredSize(Size.Empty);

    /// <summary>The row height a control claims, measured the way TableLayoutPanel measures it.</summary>
    private static int Measured(Control c)
        => (c.AutoSize || c is ComboBox ? Pref(c).Height : c.Height) + c.Margin.Vertical;

    /// <summary>The height of a note (a label as the input) wrapped at <paramref name="width"/>.</summary>
    private static int NoteHeight(Label note, int width)
        => TextRenderer.MeasureText(note.Text, note.Font, new Size(Math.Max(1, width - note.Padding.Horizontal), int.MaxValue),
                                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + note.Padding.Vertical;

    private static int WidestChild(FlowLayoutPanel flow)
        => flow.Controls.Cast<Control>().Where(c => c.Visible)
               .Select(c => Pref(c).Width + c.Margin.Horizontal).DefaultIfEmpty(0).Max() + flow.Padding.Horizontal;

    /// <summary>
    /// Height (incl. padding, excl. margin) a flow panel <paramref name="width"/> wide needs:
    /// a greedy left-to-right wrap of its visible children, as FlowLayoutPanel lays them out.
    /// </summary>
    internal static int WrappedHeight(FlowLayoutPanel flow, int width)
    {
        int inner = Math.Max(1, width - flow.Padding.Horizontal);
        int x = 0, rowH = 0, total = 0;
        foreach (Control c in flow.Controls)
        {
            if (!c.Visible) continue;
            Size p = c.AutoSize ? Pref(c) : c.Size;
            int w = Math.Max(p.Width, c.MinimumSize.Width) + c.Margin.Horizontal;
            int h = Math.Max(p.Height, c.MinimumSize.Height) + c.Margin.Vertical;
            if (x > 0 && x + w > inner) { total += rowH; x = 0; rowH = 0; }
            x += w;
            rowH = Math.Max(rowH, h);
        }
        return total + rowH + flow.Padding.Vertical;
    }
}
