#if NET11_0_OR_GREATER
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// .NET 11: a free-form window — a "console" of captioned groups instead of the tool runner's fixed
/// parameters → buttons → log. Groups go one across the window (<see cref="Wide"/>) or two side by side
/// (<see cref="Pair"/>), in the order they are added; the board scrolls when the window is too short, and an optional
/// side panel (a log, a list) sits to the right behind a draggable splitter. Each group's controls flow and wrap
/// (a single control that should fill the group, such as a table, fills it). Fonts, colours and DPI come from
/// <see cref="FormScaffold.For"/>; <see cref="AutoMinSize"/> gives the window its minimum size.
/// <code>
/// FormScaffold.For(this).Title("Console").Size(1200, 760)
///     .Board(b => b
///         .Wide("Connection", host, port, AiFgf.Primary(connect), status)
///         .Pair("Sound", [volume, mute], "Battery", [level, charging])
///         .Wide("Apps", appsTable), side: log, sideCaption: "Log")
///     .Done();
/// </code>
/// </summary>
public sealed class BoardBuilder
{
    private readonly TableLayoutPanel _root;

    internal BoardBuilder(TableLayoutPanel root) => _root = root;

    /// <summary>The board's table (two equal columns, one row per <see cref="Wide"/> or <see cref="Pair"/>).</summary>
    public TableLayoutPanel Table => _root;

    /// <summary>A group across the whole board.</summary>
    public BoardBuilder Wide(string caption, params Control[] content)
    {
        GroupBox group = Group(caption, content);
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int row = _root.RowStyles.Count - 1;
        _root.Controls.Add(group, 0, row);
        _root.SetColumnSpan(group, 2);
        _root.RowCount = _root.RowStyles.Count;   // a row is added with its group: rows without a style collapse to nothing
        return this;
    }

    /// <summary>Two groups side by side, each half the board's width: <c>.Pair("Sound", [volume, mute], "Battery", [level])</c>.</summary>
    public BoardBuilder Pair(string leftCaption, Control[] left, string rightCaption, Control[] right)
    {
        GroupBox l = Group(leftCaption, left), r = Group(rightCaption, right);
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int row = _root.RowStyles.Count - 1;
        _root.Controls.Add(l, 0, row);
        _root.Controls.Add(r, 1, row);
        _root.RowCount = _root.RowStyles.Count;
        return this;
    }

    /// <summary>Controls with a width of their own that may be narrowed to fit (the others size to their text).</summary>
    /// <remarks>A text box, drop-down or spinner sizes only its height to its font (AutoSize is true by default), so it
    /// counts whatever AutoSize says.</remarks>
    // not a spinner: narrowed, its edit box and buttons overlapped its neighbours. A track bar always: its AutoSize sets
    // only its height (a slider - auto-sized until its handle exists - was left wider than its group, cut off)
    internal static bool Shrinks(Control c) => c is TextBoxBase or ComboBox or TrackBar || (!c.AutoSize && c is ProgressBar);

    /// <summary>A captioned group in the theme's colours: its controls flow and wrap, or one control fills it.</summary>
    public static GroupBox Group(string caption, params Control[] content)
    {
        var t = AiFgfTheme.Current;
        var group = new BoardGroup
        {
            Text = caption,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 6),
            ForeColor = t.Subtle,
            BackColor = t.App,
        };
        if (content.Length == 1 && content[0] is TableLayoutPanel or DataGridView or AiFgfLog or ListBox or TabControl)
        {
            Control fill = content[0];
            fill.Dock = DockStyle.Top;
            group.Controls.Add(fill);
            return group;
        }
        // not auto-sized: an auto-sized panel took its unwrapped width, wider than the group, so its controls neither
        // wrapped where the group ends nor centred on the rows they were really in; the group sets the width (docked),
        // the panel's height follows its wrapped rows
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            WrapContents = true,
            BackColor = t.App,
        };
        bool fitting = false;
        void FitHeight()
        {
            if (fitting || flow.Width <= 0) return;
            fitting = true;
            try
            {
                int h = flow.GetPreferredSize(new Size(flow.Width, 0)).Height;
                if (flow.Height != h) flow.Height = h;
            }
            finally { fitting = false; }
        }
        flow.SizeChanged += (_, _) => FitHeight();
        flow.Layout += (_, _) => FitHeight();
        flow.ControlAdded += (_, e) => e.Control!.SizeChanged += (_, _) => FitHeight();
        // centring first: it follows each control as it is added (its size, a new font), not only the ones already there
        AiFgf.CentreRows(flow, logicalSide: 3);   // one row's labels, boxes and buttons centred; 6 px between neighbours
        var wanted = new Dictionary<Control, int>();
        foreach (Control c in content)
        {
            c.ForeColor = c is Label { ForeColor: var fc } && fc == t.Subtle ? t.Subtle : c is Button ? c.ForeColor : t.Text;
            if (Shrinks(c)) wanted[c] = c.Width;
            flow.Controls.Add(c);
        }
        // a box given more width than the group has (a 360 px text box in a 300 px half) is narrowed to the group, and
        // widened back to what it was given when there is room again - instead of being cut off at the group's edge
        void Cap()
        {
            int room = flow.ClientSize.Width - flow.Padding.Horizontal;
            if (room <= 0) return;
            foreach (var (c, w) in wanted)
            {
                // the control in the row: the box itself, or what it was wrapped in later (AiFgfFieldGlow.Wrap puts a
                // panel in its place) - narrowing the box inside a wrapper made it stick out of the wrapper
                Control item = c;
                while (item.Parent is { } p && p != flow) item = p;
                if (item.Parent != flow) continue;   // taken out of the group
                int want = item == c ? w : w + (item.Width - item.ClientSize.Width) + item.Padding.Horizontal;
                int fit = Math.Max(item.MinimumSize.Width, Math.Min(want, room - item.Margin.Horizontal));
                if (item.Width != fit) item.Width = fit;
            }
        }
        flow.ClientSizeChanged += (_, _) => Cap();
        flow.Layout += (_, _) => Cap();
        group.ClientSizeChanged += (_, _) => Cap();   // also before the first layout, and after a font or DPI change resized the boxes
        group.Controls.Add(flow);
        return group;
    }
}

/// <summary>
/// A board group that measures its content at the width it is given: a plain auto-sized GroupBox asked its content's
/// unwrapped size, so when the controls wrapped onto a second row the group stayed one row high and cut them off.
/// </summary>
internal sealed class BoardTable : TableLayoutPanel
{
    /// <summary>
    /// The narrowest the board can be with nothing cut off: every group at least as wide as its caption and its widest
    /// control that cannot shrink (boxes shrink to a small minimum), a pair's two halves each as wide as the wider of
    /// the two (the columns are equal), plus room for a vertical scroll bar. <see cref="AutoMinSize"/> keeps the window
    /// at least this wide.
    /// </summary>
    public int MinimumWidth()
    {
        int widest = 0;
        var rows = new Dictionary<int, (int Need, bool Wide)>();
        foreach (Control g in Controls)
        {
            TableLayoutPanelCellPosition at = GetPositionFromControl(g);
            if (at.Row < 0 || !g.Visible) continue;
            int need = GroupNeed(g);
            bool wide = GetColumnSpan(g) > 1;
            rows[at.Row] = rows.TryGetValue(at.Row, out var r) ? (Math.Max(r.Need, need), r.Wide || wide) : (need, wide);
        }
        foreach (var (need, wide) in rows.Values) widest = Math.Max(widest, wide ? need : 2 * need);
        return widest + Padding.Horizontal + SystemInformation.VerticalScrollBarWidth;
    }

    private int GroupNeed(Control g)
    {
        int chrome = g.Padding.Horizontal + LogicalToDeviceUnits(6) + g.Margin.Horizontal;
        int caption = TextRenderer.MeasureText(g.Text, g.Font).Width + LogicalToDeviceUnits(20) + g.Margin.Horizontal;
        int content = 0;
        foreach (Control inner in g.Controls)
        {
            if (inner is FlowLayoutPanel flow)
                foreach (Control c in flow.Controls)
                    content = Math.Max(content, (BoardBuilder.Shrinks(c) ? Math.Max(c.MinimumSize.Width, LogicalToDeviceUnits(60)) : c.GetPreferredSize(Size.Empty).Width) + c.Margin.Horizontal + flow.Padding.Horizontal);
            // a control that fills its group (tabs, a grid, a log, a list) has no width of its own: its preferred width is
            // the width it has now, so the board's minimum grew with the window (1042 px - wider than the smallest
            // screen). It needs a modest minimum; a table of controls still needs its columns.
            else if (inner is TabControl or DataGridView or AiFgfLog or ListBox or AiFgfDataGridPanel)
                content = Math.Max(content, Math.Max(inner.MinimumSize.Width, LogicalToDeviceUnits(240)) + inner.Margin.Horizontal);
            else content = Math.Max(content, inner.GetPreferredSize(Size.Empty).Width + inner.Margin.Horizontal);
        }
        return Math.Max(caption, content + chrome);
    }
}

internal sealed class BoardGroup : GroupBox
{
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Controls.Count == 0) return base.GetPreferredSize(proposedSize);
        int width = proposedSize.Width is > 0 and < int.MaxValue / 2 ? proposedSize.Width : Width;
        // what the frame and caption take: known exactly once laid out, else estimated from the font
        Size chrome = IsHandleCreated && DisplayRectangle.Width > 0
            ? new Size(Width - DisplayRectangle.Width, Height - DisplayRectangle.Height)
            : new Size(Padding.Horizontal + 6, Padding.Vertical + Font.Height + 4);
        Control content = Controls[0];
        Size inner = content.GetPreferredSize(new Size(Math.Max(1, width - chrome.Width - content.Margin.Horizontal), 0));
        return new Size(width, inner.Height + content.Margin.Vertical + chrome.Height);
    }
}

public sealed partial class FormScaffold
{
    /// <summary>The least width of a board's side panel (logical pixels): a log's find bar wraps below about this.</summary>
    public const int SideMinimum = 240;

    /// <summary>
    /// .NET 11: the window as a board of groups (<see cref="BoardBuilder"/>) instead of parameters, buttons and a body;
    /// <paramref name="side"/> (a log, say) goes to the right of it behind a splitter, in a group captioned
    /// <paramref name="sideCaption"/>. Use instead of <see cref="Parameters"/>/<see cref="Buttons"/>/<see cref="Body"/>.
    /// </summary>
    public FormScaffold Board(Action<BoardBuilder> build, Control? side = null, string sideCaption = "Log")
    {
        var t = AiFgfTheme.Current;
        var root = new BoardTable
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 0, 4, 0),
            AutoScroll = true,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            BackColor = t.App,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        build(new BoardBuilder(root));
        // a last row that takes the remaining height, so the groups stay at the top instead of spreading out
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowCount = root.RowStyles.Count;

        if (side is null) { Body(root); return this; }

        side.Dock = DockStyle.Fill;
        var sideGroup = new GroupBox { Text = sideCaption, Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8), ForeColor = t.Subtle, BackColor = t.App };
        sideGroup.Controls.Add(side);
        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = t.App, SplitterWidth = 6, FixedPanel = FixedPanel.None };
        split.Panel1.BackColor = split.Panel2.BackColor = t.App;
        split.Panel1.Controls.Add(root);
        split.Panel2.Controls.Add(sideGroup);
        // the proportions and the panels' minimums once the window has its size (a SplitContainer rejects them before)
        // the board never narrower than it needs (AutoMinSize keeps the window wide enough for both), the side at least
        // SideMinimum; 55 % for the board when the window opens, and both minimums held on every later resize
        bool placed = false;
        void Place(bool first)
        {
            int w = split.Width;
            if (w <= 0 || !split.IsHandleCreated) return;
            int min1 = root.MinimumWidth(), min2 = split.LogicalToDeviceUnits(SideMinimum);
            if (min1 + min2 + split.SplitterWidth > w)
            {
                min2 = Math.Max(1, Math.Min(min2, w - min1 - split.SplitterWidth));
                min1 = Math.Max(1, Math.Min(min1, w - min2 - split.SplitterWidth));
            }
            try
            {
                split.Panel1MinSize = min1;
                split.Panel2MinSize = min2;
                int want = first ? w * 11 / 20 : split.SplitterDistance;
                int distance = Math.Clamp(want, min1, Math.Max(min1, w - min2 - split.SplitterWidth));
                if (split.SplitterDistance != distance) split.SplitterDistance = distance;
            }
            catch (InvalidOperationException) { }   // the container refuses minimums its current width cannot hold yet
        }
        _form.Shown += (_, _) => { placed = true; Place(first: true); };
        split.SizeChanged += (_, _) => { if (placed) Place(first: false); };
        _form.DpiChanged += (_, _) => _form.BeginInvoke(() => Place(first: false));
        Body(split);
        return this;
    }
}
#endif
