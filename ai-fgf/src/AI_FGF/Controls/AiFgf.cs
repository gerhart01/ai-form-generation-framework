using System;
using System.Drawing;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// Themed control factory. Every project this framework is drawn from grew its own
/// <c>MakeButton / MakeSmallButton / StylePrimary / MakeText</c> helpers; <see cref="AiFgf"/>
/// is the shared, theme-aware version so a new form never re-writes button styling again.
///
/// All members read <see cref="AiFgfTheme.Current"/>, so switching theme restyles new controls.
/// </summary>
public static class AiFgf
{
    private static AiFgfTheme T => AiFgfTheme.Current;

    // --------------------------------------------------------------- buttons

    /// <summary>A standard flat button with hover/press feedback and a DPI-friendly minimum size.</summary>
    public static Button Button(string text, EventHandler? onClick = null)
    {
        var b = new AiFgfButton
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(96, 32), // uniform min width; grows for long labels; scales with DPI
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(4, 3, 4, 3),
            FlatStyle = FlatStyle.Flat,
            BackColor = T.Surface,
            ForeColor = T.Text,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        b.FlatAppearance.BorderColor = T.Border;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = T.HoverFill;
        b.FlatAppearance.MouseDownBackColor = T.PressFill;
        if (onClick is not null) b.Click += onClick;
        return b;
    }

    /// <summary>A compact button for inline actions (Browse…, Refresh, ✕).</summary>
    public static Button SmallButton(string text, EventHandler? onClick = null)
    {
        var b = new AiFgfButton
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(3),
            FlatStyle = FlatStyle.Flat,
            BackColor = T.Surface,
            ForeColor = T.Text,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = T.Border;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = T.HoverFill;
        if (onClick is not null) b.Click += onClick;
        return b;
    }

    /// <summary>Restyle a button as the primary (accent) action, in place. Returns it for chaining.</summary>
    public static Button Primary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = T.Accent;
        b.ForeColor = T.OnAccent;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = T.AccentHover;
        b.FlatAppearance.MouseDownBackColor = T.AccentHover;
        BoldOfParentFont(b);
        return b;
    }

    /// <summary>
    /// Give <paramref name="c"/> a bold version of its parent's font and keep it in sync. Setting
    /// a Font explicitly stops ambient inheritance, so a form-wide font change (or a theme with
    /// another family) would otherwise leave this control behind; the button usually has no
    /// parent yet when it is styled, so the font is rebuilt when it is parented and whenever the
    /// parent's font changes. Fonts created here are disposed when replaced.
    /// </summary>
    private static void BoldOfParentFont(Control c)
    {
        Font? mine = null;
        Control? watched = null;

        void Refresh(object? sender, EventArgs e)
        {
            Font source = c.Parent?.Font ?? Control.DefaultFont;
            if (mine is not null && mine.Bold && mine.Size == source.Size && mine.Unit == source.Unit
                && mine.FontFamily.Equals(source.FontFamily)) return;
            Font next = new(source, source.Style | FontStyle.Bold);
            Font? old = mine;
            mine = next;
            c.Font = next;
            old?.Dispose();
        }

        void Rewire(object? sender, EventArgs e)
        {
            if (watched is not null) watched.FontChanged -= Refresh;
            watched = c.Parent;
            if (watched is not null) watched.FontChanged += Refresh;
            Refresh(sender, e);
        }

        c.ParentChanged += Rewire;
        c.Disposed += (_, _) =>
        {
            if (watched is not null) watched.FontChanged -= Refresh;
            mine?.Dispose();
        };
        Refresh(null, EventArgs.Empty);
    }

    /// <summary>A primary button in one call.</summary>
    public static Button PrimaryButton(string text, EventHandler? onClick = null)
        => Primary(Button(text, onClick));

    // ---------------------------------------------------------------- inputs

    public static TextBox Text(string? placeholder = null) => new()
    {
        Dock = DockStyle.Fill,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(3, 4, 3, 4),   // symmetric top/bottom so the field centres on its label at every DPI
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = T.Surface,
        ForeColor = T.Text,
        PlaceholderText = placeholder ?? string.Empty,
    };

    /// <summary>
    /// Keep <paramref name="box"/> at most <paramref name="logicalWidth"/> wide (scaled to DPI) but no
    /// wider than what its wrapping bar has left next to the other controls: on a narrow window it
    /// shrinks (down to <paramref name="minLogicalWidth"/>) so the bar stays on one row and the
    /// space below it (a log, a grid) keeps its lines, instead of sticking out or wrapping.
    /// </summary>
    public static void FitWidthToBar(TextBox box, FlowLayoutPanel bar, int logicalWidth, int minLogicalWidth = 80)
    {
        void Fit()
        {
            // in a hidden tab every child reads Visible=false, so the others would count as 0 px wide: fit once shown
            if (!bar.Visible) return;
            int others = bar.Controls.Cast<Control>().Where(c => c != box && c.Visible)
                .Sum(c => c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal);
            int room = bar.ClientSize.Width - bar.Padding.Horizontal - box.Margin.Horizontal - others;
            int w = Math.Max(box.LogicalToDeviceUnits(minLogicalWidth), Math.Min(box.LogicalToDeviceUnits(logicalWidth), room));
            if (box.Width == w) return;
            box.Width = w;
            // the bar may now wrap differently: let its container recompute the bar's auto height
            // (otherwise a button that moved to a second row sticks out of the one-row-high bar)
            if (bar.IsHandleCreated) bar.BeginInvoke(() => { if (!bar.IsDisposed) bar.Parent?.PerformLayout(); });
        }
        bar.Resize += (_, _) => Fit();
        bar.VisibleChanged += (_, _) => Fit();
        box.DpiChangedAfterParent += (_, _) => Fit();
        Fit();
    }

    /// <summary>
    /// Centre every control of a one-line tool bar (find bar, filter bar) on the tallest one: a FlowLayoutPanel
    /// puts tops in line, so a label and a text box sat above the buttons' centre — 8 px at 150 %, because
    /// fixed top margins only matched at 100 %. Each control gets a top/bottom margin of
    /// <paramref name="logicalGap"/> plus half its height difference to the tallest; redone when the DPI,
    /// the font or the controls change. Left and right margins stay what each control had when added (or
    /// <paramref name="logicalSide"/>), taken as logical pixels and scaled to the DPI — a flow panel does not scale
    /// its children's margins, so buttons 6 px apart at 100 % were still 6 px apart at 200 %.
    /// </summary>
    public static void CentreRows(FlowLayoutPanel bar, int logicalGap = 3, int? logicalSide = null)
    {
        bool busy = false;
        var sides = new Dictionary<Control, (int Left, int Right)>();
        void Centre()
        {
            if (busy) return;
            busy = true;
            try
            {
                var kids = bar.Controls.Cast<Control>().ToList();
                if (kids.Count == 0) return;
                int Height(Control c) => c.AutoSize || c is TextBox ? c.GetPreferredSize(Size.Empty).Height : c.Height;
                // the window's DPI: a nested panel's DeviceDpi can lag behind it after a DPI change (120 in a 240-DPI form)
                int dpi = bar.FindForm()?.DeviceDpi ?? bar.DeviceDpi;
                int Px(int logical) => (int)Math.Round(logical * dpi / 96.0, MidpointRounding.AwayFromZero);
                int gap = Px(logicalGap);
                // each wrapped row is centred on its own tallest control: centred on the tallest of the whole panel, a row
                // of short controls under a row with a two-line button sat 12 px low against its neighbours. The rows are
                // worked out the way the panel wraps (widths and side margins only, which centring does not change).
                var rowOf = new Dictionary<Control, int>();
                int avail = bar.ClientSize.Width - bar.Padding.Horizontal, x = 0, row = 0;
                foreach (Control c in kids)
                {
                    (int l0, int r0) = logicalSide is int s0 ? (s0, s0) : sides.TryGetValue(c, out var o0) ? o0 : (c.Margin.Left, c.Margin.Right);
                    int w = c.Width + Px(l0) + Px(r0);
                    if (bar.WrapContents && avail > 0 && x > 0 && x + w > avail) { row++; x = 0; }
                    rowOf[c] = row;
                    x += w;
                    if (bar.GetFlowBreak(c)) { row++; x = 0; }
                }
                var tallestOf = kids.GroupBy(c => rowOf[c]).ToDictionary(g => g.Key, g => g.Max(Height));
                foreach (Control c in kids)
                {
                    int spare = tallestOf[rowOf[c]] - Height(c);
                    (int l, int r) = logicalSide is int side ? (side, side) : sides.TryGetValue(c, out var own) ? own : (c.Margin.Left, c.Margin.Right);
                    int left = Px(l), right = Px(r);
                    var m = new Padding(left, gap + spare / 2, right, gap + spare - spare / 2);
                    if (c.Margin != m) c.Margin = m;
                }
            }
            finally { busy = false; }
        }
        bar.ControlAdded += (_, e) =>
        {
            Control c = e.Control!;
            sides[c] = (c.Margin.Left, c.Margin.Right);   // as built: logical
            Centre();
            c.SizeChanged += (_, _) => Centre();   // a new font changes heights
        };
        bar.ControlRemoved += (_, e) => sides.Remove(e.Control!);
        bar.DpiChangedAfterParent += (_, _) => Centre();
        bar.ClientSizeChanged += (_, _) => Centre();   // a new width can wrap the controls into other rows
        bar.FontChanged += (_, _) => Centre();
        bar.HandleCreated += (_, _) => Centre();
        Centre();
    }

    /// <summary>
    /// Make a horizontal track bar exactly as high as its thumb (with the thumb's own top inset below it): an
    /// auto-sized one is 56 px with the thumb in its top 26, so a label centred on the row sat 13 px below the
    /// thumb. Redone whenever the height is changed (a DPI change rescales it). Use with <see cref="TickStyle.None"/> — ticks need the room.
    /// </summary>
    public static void FitTrackBarToThumb(TrackBar bar)
    {
        void Fit()
        {
            if (!bar.IsHandleCreated) return;
            Rectangle thumb = ThumbRect(bar);
            if (thumb.Height <= 0) return;
            bar.AutoSize = false;
            int h = thumb.Bottom + thumb.Top;
            if (bar.Height != h) bar.Height = h;
        }
        bar.HandleCreated += (_, _) => Fit();
        bar.SizeChanged += (_, _) => Fit();   // a DPI change rescales the height; the thumb does not follow it
        Fit();
    }

    private const int TBM_GETTHUMBRECT = 0x0400 + 25;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessage")]
    private static extern IntPtr SendRect(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    /// <summary>Where a track bar draws its thumb (client coordinates).</summary>
    internal static Rectangle ThumbRect(TrackBar t)
    {
        var r = new RECT();
        SendRect(t.Handle, TBM_GETTHUMBRECT, IntPtr.Zero, ref r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>A themed numeric spinner (replaces a free-text box for counts, ports, timeouts…).</summary>
    public static NumericUpDown Number(decimal min = 0, decimal max = 100, decimal value = 0, int decimals = 0) => new AiFgfNumber
    {
        AutoSize = true,          // height follows the font (predictable in TableLayoutPanel rows)
        Dock = DockStyle.Fill,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(3, 4, 3, 4),   // symmetric top/bottom so the field centres on its label at every DPI
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = T.Surface,
        ForeColor = T.Text,
        Minimum = min,
        Maximum = max,
        DecimalPlaces = decimals,
        Value = Math.Clamp(value, min, max),
        ThousandsSeparator = false,
    };

#if NET11_0_OR_GREATER
    /// <summary>.NET 11: an on/off switch (<see cref="AiFgfToggle"/>) — a check box drawn as a pill with a sliding knob.</summary>
    public static CheckBox Toggle(string text, bool on = false) => new AiFgfToggle
    {
        Text = text,
        Checked = on,
        Margin = new Padding(0, 6, 16, 0),
        ForeColor = T.Text,
    };
#endif

    /// <summary>
    /// Give <paramref name="root"/> and every control inside it the button style <paramref name="look"/>: the buttons and,
    /// on .NET 11, every other control with a Direct3D look - progress bars, tab headers, spin buttons, switches, sliders, radio buttons, check boxes, notes.
    /// ToolForm does this itself; a form built with FormScaffold calls it (again after <c>AiFgfEffects.Changed</c>).
    /// </summary>
#if NET11_0_OR_GREATER
    public
#else
    internal
#endif
    static void ApplyLook(Control root, AiFgfButtonStyle look)
    {
        Set(root);
        foreach (Control c in root.Controls) ApplyLook(c, look);

        void Set(Control c)
        {
            switch (c)
            {
                case AiFgfButton b: b.Look = look; break;
#if NET11_0_OR_GREATER
                case AiFgfProgressBar p: p.Look = look; break;
                case AiFgfTabControl tabs: tabs.Look = look; break;
                case AiFgfNumber number: number.Look = look; break;
                case AiFgfToggle toggle: toggle.Look = look; break;
                case AiFgfSlider slider: slider.Look = look; break;
                case AiFgfRadio radio: radio.Look = look; break;
                case AiFgfCheck check: check.Look = look; break;
                case AiFgfNote note: note.Look = look; break;
#endif
            }
        }
    }

    public static CheckBox Check(string text, bool @checked = false) => new AiFgfCheck
    {
        Text = text,
        AutoSize = true,
        Checked = @checked,
        Margin = new Padding(0, 6, 16, 0),
        ForeColor = T.Text,
    };

#if NET11_0_OR_GREATER
    /// <summary>.NET 11: a note (<see cref="AiFgfNote"/>) - a line of text; a Direct3D plate in the Raised and Glow looks.</summary>
    public static Label Note(string text) => new AiFgfNote { Text = text };
#endif

    public static Label Label(string text, bool subtle = false) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = subtle ? T.Subtle : T.Text,
        Margin = new Padding(0, 6, 6, 0),
    };

    /// <summary>An editable combo with type-ahead over its own items (a feature picker).</summary>
    public static ComboBox Combo(bool editable = true)
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3, 4, 3, 4),   // symmetric top/bottom so the field centres on its label at every DPI
            DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
            AutoCompleteMode = editable ? AutoCompleteMode.SuggestAppend : AutoCompleteMode.None,
            AutoCompleteSource = editable ? AutoCompleteSource.ListItems : AutoCompleteSource.None,
            // Standard, not Flat: WinForms paints a flat combo's drop-down button in the light system
            // colour whatever the theme; the standard one is drawn by the window theme (dark via AiFgfNativeTheme)
            FlatStyle = FlatStyle.Standard,
            BackColor = T.Surface,
            ForeColor = T.Text,
        };
        combo.DropDown += (_, _) => FitDropDownWidth(combo);
        return combo;
    }

    /// <summary>
    /// Widen a drop-down's list to its longest item (up to the screen) so no item is cut off when
    /// the list opens; the box itself keeps the width its row gives it.
    /// </summary>
    public static void FitDropDownWidth(ComboBox combo)
    {
        int widest = 0;
        foreach (object? item in combo.Items) widest = Math.Max(widest, TextRenderer.MeasureText(combo.GetItemText(item), combo.Font).Width);
        int need = widest + SystemInformation.VerticalScrollBarWidth + combo.LogicalToDeviceUnits(8);
        int screen = Screen.FromControl(combo).WorkingArea.Width;
        combo.DropDownWidth = Math.Max(combo.Width, Math.Min(need, screen));
    }

    // --------------------------------------------------------- browse dialogs
    // The reference projects open the same three dialogs over and over; these return
    // the chosen path (or null if cancelled) so callers stay one line long.

    public static string? PickOpenFile(string? filter = null, string? title = null)
    {
        using var dlg = new OpenFileDialog { Filter = filter ?? AiFgfText.AllFilesFilter, Title = title ?? AiFgfText.SelectFile };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    public static string? PickSaveFile(string? filter = null, string? title = null, string? defaultName = null)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = filter ?? AiFgfText.AllFilesFilter,
            Title = title ?? AiFgfText.SaveAs,
            FileName = defaultName ?? string.Empty,
        };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    public static string? PickFolder(string? description = null)
    {
        using var dlg = new FolderBrowserDialog { Description = description ?? AiFgfText.SelectFolder };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
    }

    /// <summary>Wire a "Browse…" button that drops the chosen file path into <paramref name="target"/>.</summary>
    public static Button BrowseFileInto(TextBox target, string? filter = null, string? title = null)
        => SmallButton(AiFgfText.Browse, (_, _) => { if (PickOpenFile(filter, title) is { } p) target.Text = p; });

    /// <summary>Wire a "Browse…" button that drops the chosen folder into <paramref name="target"/>.</summary>
    public static Button BrowseFolderInto(TextBox target, string? description = null)
        => SmallButton(AiFgfText.Browse, (_, _) => { if (PickFolder(description) is { } p) target.Text = p; });
}
