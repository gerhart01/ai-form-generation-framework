using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>A layout problem found by <see cref="AiFgfConformance"/>.</summary>
public sealed record ConformanceIssue(string Where, string Message)
{
    public override string ToString() => $"{Where}: {Message}";
}

/// <summary>
/// "Is this form correct?" — a layout conformance check usable on any form (generated from a
/// <see cref="ToolSpec"/> or hand-written), in tests or in a debug build. At the form's minimum
/// size, its current size and a wide size, on every tab page, it reports: overlapping sibling
/// controls, buttons outside the window, clipped labels, collapsed controls, a missing
/// MinimumSize, and spacing (<see cref="CheckSpacing"/>: overlap across containers, gaps too small,
/// rows not centred, uneven button gaps, items on the window's edge). <see cref="CheckAcrossDpi"/> repeats that after moving the form to other DPIs
/// with a real <c>WM_DPICHANGED</c>, the same rescale a user triggers by changing monitors.
/// </summary>
public static class AiFgfConformance
{
    /// <summary>
    /// The smallest working area, in logical (96-DPI) pixels, a form must fit at its minimum size:
    /// 1280x720 at 125 % (1024x576) minus a 48-px taskbar. Every resolution/scale pair Windows
    /// offers has at least this much room.
    /// </summary>
    public static Size SmallestScreen { get => AutoMinSize.SmallestScreen; set => AutoMinSize.SmallestScreen = value; }

    /// <summary>Minimum contrast ratio of a control's text colour against its background (WCAG AA: 4.5).</summary>
    public static double MinTextContrast { get; set; } = 4.5;

    /// <summary>Minimum contrast of the strongest rendered text pixel against the background, disabled text included.</summary>
    public static double MinRenderedContrast { get; set; } = 3.0;

    /// <summary>Check a shown form at its current DPI. The form's size and selected tabs are restored.</summary>
    public static IReadOnlyList<ConformanceIssue> Check(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var issues = new List<ConformanceIssue>();
        if (!form.Visible) { issues.Add(new("form", "must be shown before it can be checked")); return issues; }
        if (form.MinimumSize.Width <= 0 || form.MinimumSize.Height <= 0)
            issues.Add(new("form", "has no MinimumSize — it can be shrunk until its controls overlap (use AutoMinSize.Guard)"));
        else
        {
            // in logical units, so the same limit holds at every scale
            double k = form.DeviceDpi / 96.0;
            Size logical = new((int)Math.Ceiling(form.MinimumSize.Width / k), (int)Math.Ceiling(form.MinimumSize.Height / k));
            if (logical.Width > SmallestScreen.Width || logical.Height > SmallestScreen.Height)
                issues.Add(new("form", $"minimum size {logical.Width}x{logical.Height} (logical) does not fit the smallest supported screen " +
                                       $"{SmallestScreen.Width}x{SmallestScreen.Height} — part of the form would be off-screen"));
        }

        Size original = form.Size;
        FormWindowState state = form.WindowState;
        if (state != FormWindowState.Normal) form.WindowState = FormWindowState.Normal;
        var sizes = new List<(string Name, Size Size)>();
        if (form.MinimumSize.Width > 0 && form.MinimumSize.Height > 0) sizes.Add(("minimum size", form.MinimumSize));
        sizes.Add(("initial size", original));
        sizes.Add(("wide size", new Size(Math.Max(original.Width, form.MinimumSize.Width) * 3 / 2, Math.Max(original.Height, form.MinimumSize.Height))));

        var seen = new HashSet<string>();
        try
        {
            foreach (var (name, size) in sizes)
            {
                Resize(form, size);
                Settle(form);
                foreach (ConformanceIssue issue in CheckEveryTab(form))
                    if (seen.Add(issue.ToString())) issues.Add(issue with { Where = $"{name} {form.Width}x{form.Height}: {issue.Where}" });
            }
        }
        finally
        {
            form.Size = original;
            if (state != FormWindowState.Normal) form.WindowState = state;
            Settle(form);
        }
        return issues;
    }

    /// <summary>
    /// Create the form with <paramref name="create"/>, show it off-screen, move it to each DPI in
    /// turn with a real WM_DPICHANGED and <see cref="Check"/> it there. The form is disposed.
    /// </summary>
    public static IReadOnlyList<ConformanceIssue> CheckAcrossDpi(Func<Form> create, params int[] dpis)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (dpis.Length == 0) dpis = new[] { 96, 120, 144, 192 };
        var issues = new List<ConformanceIssue>();
        foreach (int dpi in dpis)
        {
            using Form form = create();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-5000, -5000);
            form.ShowInTaskbar = false;
            form.Show();
            Pump();
            if (form.DeviceDpi != dpi) MoveToDpi(form, dpi);
            if (form.DeviceDpi != dpi) issues.Add(new($"{dpi} DPI", $"the form did not accept the DPI change (DeviceDpi {form.DeviceDpi})"));
            issues.AddRange(Check(form).Select(i => i with { Where = $"{dpi} DPI, {i.Where}" }));
            form.Close();
        }
        return issues;
    }

    // ------------------------------------------------------------------ checks

    private static IEnumerable<ConformanceIssue> CheckEveryTab(Form form)
    {
        foreach (ConformanceIssue i in CheckTree(form, form)) yield return i;
        foreach (ConformanceIssue i in CheckRenderedText(form)) yield return i;
        foreach (ConformanceIssue i in CheckSpacing(form)) yield return i;
        foreach (TabControl tabs in Descendants(form).OfType<TabControl>().ToList())
        {
            int selected = tabs.SelectedIndex;
            for (int p = 0; p < tabs.TabCount; p++)
            {
                if (p == selected) continue;
                tabs.SelectedIndex = p;
                Settle(form);
                foreach (ConformanceIssue i in CheckTree(form, tabs.TabPages[p])) yield return i with { Where = $"tab '{tabs.TabPages[p].Text}': {i.Where}" };
                foreach (ConformanceIssue i in CheckRenderedText(form)) yield return i with { Where = $"tab '{tabs.TabPages[p].Text}': {i.Where}" };
                foreach (ConformanceIssue i in CheckSpacing(form)) yield return i with { Where = $"tab '{tabs.TabPages[p].Text}': {i.Where}" };
            }
            tabs.SelectedIndex = selected;
            Settle(form);
        }
    }

    private static IEnumerable<ConformanceIssue> CheckTree(Form form, Control root)
    {
        Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        foreach (Control parent in new[] { root }.Concat(Descendants(root)))
        {
            if (!parent.Visible) continue;
            var kids = parent.Controls.Cast<Control>().Where(c => c.Visible).ToList();
            for (int i = 0; i < kids.Count; i++)
                for (int j = i + 1; j < kids.Count; j++)
                {
                    Rectangle overlap = Rectangle.Intersect(kids[i].Bounds, kids[j].Bounds);
                    if (overlap.Width > 1 && overlap.Height > 1)
                        yield return new(Name(kids[i]), $"overlaps {Name(kids[j])}");
                }

            foreach (Control c in kids)
            {
                foreach (ConformanceIssue t in CheckText(form, c)) yield return t;
                // an empty AutoSize label (a status/counter not set yet) is legitimately zero-width
                bool emptyAutoLabel = c is Label { AutoSize: true } && c.Text.Length == 0;
                if ((c.Width <= 2 || c.Height <= 2) && !emptyAutoLabel)
                    yield return new(Name(c), $"collapsed to {c.Width}x{c.Height}");
                else if (c is Label { AutoEllipsis: false } label && label.Text.Length > 0)
                {
                    Size text = LabelTextSize(label);
                    if (text.Width > label.ClientSize.Width + 2 || label.Right > parent.ClientSize.Width + 1)
                        yield return new(Name(c), $"text is clipped (needs {text.Width}px, has {label.ClientSize.Width}px)");
                }
            }
        }
    }

    // ------------------------------------------------------------ text checks

    /// <summary>Does the text of <paramref name="c"/> fit, stay inside its containers, and contrast with its background?</summary>
    private static IEnumerable<ConformanceIssue> CheckText(Form form, Control c)
    {
        bool hasText = c.Text.Length > 0;
        bool textual = c is Label or ButtonBase or GroupBox or TextBoxBase or ComboBox or UpDownBase;
        if (!textual || c.Width <= 2 || c.Height <= 2) yield break;

        // 1. inside every container (and the window), unless a container scrolls
        Rectangle screen = c.Parent!.RectangleToScreen(c.Bounds);
        for (Control? p = c.Parent; p is not null; p = p.Parent)
        {
            if (p is ScrollableControl { AutoScroll: true }) break;
            Rectangle room = p.RectangleToScreen(p.ClientRectangle);
            room.Inflate(1, 1);
            if (!room.Contains(screen))
            {
                yield return new(Name(c), p == form ? "is not fully inside the window" : $"sticks out of {Name(p)} — part of it is cut off");
                break;
            }
        }

        // 2. the text fits the control
        int fontH = c.Font.Height;
        switch (c)
        {
            case CheckBox or RadioButton when hasText:
            {
                int glyph = c.LogicalToDeviceUnits(13) + c.LogicalToDeviceUnits(3);
                Size text = CaptionSize(c, c.ClientSize.Width - glyph);
                if (glyph + text.Width > c.ClientSize.Width + 2 || text.Height > c.ClientSize.Height + 2)
                    yield return new(Name(c), $"text is clipped (needs {glyph + text.Width}x{text.Height}px, has {c.ClientSize.Width}x{c.ClientSize.Height}px)");
                break;
            }
            case ButtonBase when hasText:
            {
                Size text = CaptionSize(c, c.ClientSize.Width - c.Padding.Horizontal);
                if (text.Width > c.ClientSize.Width - 4 || text.Height > c.ClientSize.Height)
                    yield return new(Name(c), $"text is clipped (needs {text.Width}x{text.Height}px, has {c.ClientSize.Width}x{c.ClientSize.Height}px)");
                break;
            }
            case GroupBox when hasText:
            {
                int need = TextRenderer.MeasureText(c.Text, c.Font).Width + c.LogicalToDeviceUnits(16);
                if (need > c.Width) yield return new(Name(c), $"caption is clipped (needs {need}px, has {c.Width}px)");
                break;
            }
            case Label { AutoEllipsis: false } label when hasText:
            {
                Size text = LabelTextSize(label);
                if (text.Height > c.ClientSize.Height + 1)
                    yield return new(Name(c), $"text is clipped — cut vertically (needs {text.Height}px, has {c.ClientSize.Height}px)");
                break;
            }
            case TextBoxBase { Multiline: true } box:
            {
                if (box.WordWrap)
                    yield return new(Name(c), "wraps long lines — they should stay one line and scroll horizontally (WordWrap = false)");
                int lines = c is AiFgfMemo memo ? memo.ShownLines : c.Parent is AiFgfLog ? 3 : 1;   // a memo shows its lines; a log at least 3
                if (box.ClientSize.Height < lines * fontH)
                    yield return new(Name(c), $"shows less than {lines} full line(s) ({box.ClientSize.Height}px for {fontH}px lines)");
                break;
            }
            case TextBoxBase or ComboBox or UpDownBase:
                if (c.ClientSize.Height < fontH)
                    yield return new(Name(c), $"is shorter than its text ({c.ClientSize.Height}px for a {fontH}px font)");
                break;
        }

        // 3. colour contrast of enabled text (disabled text is checked on the rendered pixels)
        bool systemDrawn = c is ComboBox or UpDownBase
            || (c is ButtonBase { FlatStyle: FlatStyle.System or FlatStyle.Standard } && c is not CheckBox and not RadioButton);
        if (c.Enabled && !systemDrawn && (hasText || c is TextBoxBase))
        {
            double ratio = Contrast(c.ForeColor, EffectiveBackColor(c));
            if (ratio < MinTextContrast)
                yield return new(Name(c), $"text colour contrast {ratio:0.0}:1 is below {MinTextContrast:0.0}:1 — hard to read (worse on HDR/bright displays)");
        }
    }

    // --------------------------------------------------------------- spacing

    /// <summary>
    /// Spacing between what the user sees, across containers, as drawn (in screen pixels, clipped by every
    /// container): two items overlap; neighbours in a row or a column are closer than the minimum gap; a label
    /// or input is not centred on its row neighbour; the gaps of a button row are uneven; an item touches the
    /// window's edge. Limits are logical pixels (scaled to the DPI): 4 between controls, 2 next to a label,
    /// centres within 2, button gaps within 2, 3 from the window's left/top/right edge.
    /// </summary>
    public static IReadOnlyList<ConformanceIssue> CheckSpacing(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        double k = form.DeviceDpi / 96.0;
        int Px(double logical) => (int)Math.Round(logical * k);
        var items = Items(form).ToList();
        var clipped = items.Where(x => x.Bounds.Size != x.Control.Size).Select(x => x.Control).ToHashSet();
        var issues = new List<ConformanceIssue>();

        for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
            {
                var (a, ra) = items[i];
                var (b, rb) = items[j];
                Rectangle o = Rectangle.Intersect(ra, rb);
                if (o.Width > 1 && o.Height > 1)
                {
                    if (a.Parent != b.Parent) issues.Add(new(Name(a), $"overlaps {Name(b)} (in another container) by {o.Width}x{o.Height}px"));
                    continue;
                }
                if (clipped.Contains(a) || clipped.Contains(b)) continue;   // partly scrolled away: its centre is not where it is drawn
                bool label = a is Label || b is Label;
                int vOverlap = Math.Min(ra.Bottom, rb.Bottom) - Math.Max(ra.Top, rb.Top);
                int hOverlap = Math.Min(ra.Right, rb.Right) - Math.Max(ra.Left, rb.Left);
                if (vOverlap * 2 >= Math.Min(ra.Height, rb.Height))   // the same row
                {
                    var (l, rl, r, rr) = ra.Left <= rb.Left ? (a, ra, b, rb) : (b, rb, a, ra);
                    int gap = rr.Left - rl.Right;
                    if (gap >= 0 && gap < Px(label ? 2 : 4))
                        issues.Add(new(Name(l), $"is {gap}px from {Name(r)} — too close (at least {Px(label ? 2 : 4)}px)"));
                    if (gap >= 0 && (l is Label || gap < Px(40)) && OneLine(l, rl) && OneLine(r, rr) && Nearest(items, l, rl, rr))
                    {
                        int dc = Math.Abs(Centre(l, rl) - Centre(r, rr));
                        // a caption wrapped onto more lines than its neighbour may instead line up at the top
                        bool wrapped = Wraps(l, rl) != Wraps(r, rr);
                        bool topAligned = wrapped && Math.Abs(rl.Top - rr.Top) <= Px(6);
                        // a track bar keeps its thumb's metrics from the DPI its handle was made at (a Win32 limit)
                        int tolerance = l is TrackBar || r is TrackBar ? Px(4) : Px(2);
                        if (dc > tolerance && !topAligned) issues.Add(new(Name(l), $"is not centred on {Name(r)} beside it (centres {dc}px apart; {rl.Top - rr.Top:+0;-0;0}px top, {rl.Height} vs {rr.Height}px high)"));
                    }
                }
                else if (hOverlap * 2 >= Math.Min(ra.Width, rb.Width) && !label)   // the same column
                {
                    int gap = Math.Max(ra.Top, rb.Top) - Math.Min(ra.Bottom, rb.Bottom);
                    if (gap >= 0 && gap < Px(2))
                        issues.Add(new(Name(ra.Top <= rb.Top ? a : b), $"is {gap}px from {Name(ra.Top <= rb.Top ? b : a)} below it — too close (at least {Px(2)}px)"));
                }
            }

        // button rows: the same parent, the same centre line, gaps alike
        foreach (var row in items.Where(x => x.Control is ButtonBase and not CheckBox and not RadioButton)
                                 .GroupBy(x => (x.Control.Parent, Centre: (x.Bounds.Top + x.Bounds.Bottom) / 2 / Math.Max(1, Px(4)))))
        {
            var sorted = row.OrderBy(x => x.Bounds.Left).ToList();
            if (sorted.Count < 3) continue;
            var gaps = sorted.Zip(sorted.Skip(1), (x, y) => y.Bounds.Left - x.Bounds.Right).Where(g => g >= 0).ToList();
            if (gaps.Count < 2) continue;
            int min = gaps.Min();
            var near = gaps.Where(g => g <= Math.Max(min * 3, Px(12))).ToList();   // a wide gap separates groups on purpose
            if (near.Count >= 2 && near.Max() - near.Min() > Px(2))
                issues.Add(new(Name(sorted[0].Control) + " row", $"uneven gaps between buttons: {string.Join(", ", gaps)}px"));
        }

        // the window's edges (the bottom has the status bar)
        Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        foreach (var (c, r) in items)
        {
            int edge = Px(3);
            if (r.Left - client.Left < edge || client.Right - r.Right < edge || r.Top - client.Top < edge)
                issues.Add(new(Name(c), $"touches the window's edge ({r.Left - client.Left}/{r.Top - client.Top}/{client.Right - r.Right}px from left/top/right)"));
        }
        return issues;
    }

    /// <summary>What the user sees as one thing, with its on-screen rectangle clipped by every container.</summary>
    private static IEnumerable<(Control Control, Rectangle Bounds)> Items(Form form)
    {
        foreach (Control c in Descendants(form))
        {
            bool item = c is Label { Text.Length: > 0 } or ButtonBase or TextBoxBase or ComboBox or UpDownBase or TrackBar or ListBox or ProgressBar;
            if (!item || !c.Visible || c.Parent is UpDownBase || c.Parent is AiFgfLog && c is TextBoxBase { Multiline: true }) continue;
            Rectangle r = c.Parent!.RectangleToScreen(c.Bounds);
            for (Control? p = c.Parent; p is not null && !r.IsEmpty; p = p.Parent)
                r.Intersect(p.RectangleToScreen(p.ClientRectangle));
            if (r.Width > 2 && r.Height > 2) yield return (c, r);
        }
    }

    /// <summary>A caption on more than one line.</summary>
    private static bool Wraps(Control c, Rectangle r) => c is Label or ButtonBase && c.Text.Length > 0 && r.Height >= c.Font.Height * 7 / 4;

    private static bool OneLine(Control c, Rectangle r)
        => c is TrackBar || c is not TextBoxBase { Multiline: true } and not ListBox && r.Height < c.Font.Height * 2;

    /// <summary>The text's centre line: a label's text sits inside its padding.</summary>
    private static int Centre(Control c, Rectangle r) => c switch
    {
        Label l => r.Top + l.Padding.Top + (r.Height - l.Padding.Vertical) / 2,
        TrackBar t when t.IsHandleCreated => t.RectangleToScreen(AiFgf.ThumbRect(t)) is var thumb ? (thumb.Top + thumb.Bottom) / 2 : 0,   // the thumb's line, not the control's
        _ => (r.Top + r.Bottom) / 2,
    };

    /// <summary>Nothing between the two in the row.</summary>
    private static bool Nearest(List<(Control Control, Rectangle Bounds)> items, Control l, Rectangle rl, Rectangle rr)
        => !items.Any(x => x.Bounds.Left >= rl.Right && x.Bounds.Right <= rr.Left && x.Bounds.Bottom > Math.Max(rl.Top, rr.Top) && x.Bounds.Top < Math.Min(rl.Bottom, rr.Bottom));

    /// <summary>A caption on one line, or — when it does not fit <paramref name="width"/> — wrapped at it (multi-line buttons/check boxes).</summary>
    private static Size CaptionSize(Control c, int width)
    {
        Size line = TextRenderer.MeasureText(c.Text, c.Font);
        if (line.Width <= width + 2 || c.Height < c.Font.Height * 2) return line;
        return TextRenderer.MeasureText(c.Text, c.Font, new Size(Math.Max(1, width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
    }

    /// <summary>
    /// The size a label's text needs: on one line, or — for a label that wraps (a maximum width,
    /// as ParamTable gives long labels) — wrapped at its width, so a 3-line label is not "clipped".
    /// </summary>
    private static Size LabelTextSize(Label label)
    {
        bool wraps = label.MaximumSize.Width > 0 || !label.AutoSize;
        return wraps
            ? TextRenderer.MeasureText(label.Text, label.Font, new Size(Math.Max(1, label.ClientSize.Width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl)
            : TextRenderer.MeasureText(label.Text, label.Font);
    }

    /// <summary>
    /// The window as Windows shows it (PrintWindow): each child clipped to its parent. DrawToBitmap draws children
    /// whole, so a field scrolled out of a parameters area is painted over whatever lies below it (the log's find
    /// bar), and captions there read as invisible. DrawToBitmap only if PrintWindow fails.
    /// </summary>
    private static void Render(Form form, Bitmap bmp)
    {
        bool printed = false;
        if (OperatingSystem.IsWindows())
        {
            using Graphics g = Graphics.FromImage(bmp);
            IntPtr dc = g.GetHdc();
            try { printed = PrintWindow(form.Handle, dc, 0); }   // 0: the window paints itself now (PW_RENDERFULLCONTENT reads the DWM frame, stale right after a resize)
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
            finally { g.ReleaseHdc(dc); }
        }
        if (!printed) form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
    }


    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    /// <summary>Render the form and verify every caption is actually drawn and distinguishable from its background.</summary>
    private static IEnumerable<ConformanceIssue> CheckRenderedText(Form form)
    {
        if (form.Width <= 0 || form.Height <= 0) yield break;
        using var bmp = new Bitmap(form.Width, form.Height);
        Render(form, bmp);
        Point clientOrigin = form.PointToScreen(Point.Empty);
        Point offset = new(clientOrigin.X - form.Left, clientOrigin.Y - form.Top);   // client -> bitmap

        // tab headers: each caption must be readable in its tab
        foreach (TabControl tabs in Descendants(form).OfType<TabControl>())
        {
            if (!tabs.Visible) continue;
            for (int i = 0; i < tabs.TabCount; i++)
            {
                Rectangle tab = tabs.RectangleToScreen(tabs.GetTabRect(i));
                Point at = form.PointToClient(tab.Location);
                var rect = Rectangle.Intersect(new Rectangle(at.X + offset.X, at.Y + offset.Y, tab.Width, tab.Height), new Rectangle(Point.Empty, bmp.Size));
                if (rect.Width <= 2 || rect.Height <= 2) continue;
                rect.Inflate(-2, -2);
                double best = StrongestContrast(bmp, rect);
                if (best < MinRenderedContrast)
                    yield return new($"TabControl tab '{tabs.TabPages[i].Text}'", $"rendered caption is not visible (strongest contrast {best:0.0}:1)");
            }
        }

        foreach (Control c in Descendants(form))
        {
            if (!c.Visible || c.Text.Trim().Length == 0 || c is not (Label or ButtonBase)) continue;
            if (c.Width <= 2 || c.Height <= 2) continue;
            // only the part not clipped by a container (rows scrolled out of view are reachable by scrolling)
            Rectangle whole = c.RectangleToScreen(c.ClientRectangle);
            Rectangle visible = whole;
            bool scrolls = false;
            for (Control? p = c.Parent; p is not null; p = p.Parent)
            {
                visible.Intersect(p.RectangleToScreen(p.ClientRectangle));
                scrolls |= p is ScrollableControl { AutoScroll: true };
            }
            if (visible.Width <= 2 || visible.Height <= 2) continue;   // outside: reported by CheckText, or scrolled away
            if (scrolls && visible != whole) continue;                  // at the edge of a scrolled area: scrolling shows it
            Point at = form.PointToClient(visible.Location);
            var rect = Rectangle.Intersect(new Rectangle(at.X + offset.X, at.Y + offset.Y, visible.Width, visible.Height),
                                           new Rectangle(Point.Empty, bmp.Size));
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            double best = StrongestContrast(bmp, rect);
            // ClearType draws the one-pixel stems of a small caption ("iiii" at 7 pt) without one full-strength pixel:
            // an enabled caption in colours that pass is held to a little less — text that is really missing stays far below
            double need = c.Enabled && Contrast(c.ForeColor, EffectiveBackColor(c)) >= MinTextContrast ? MinRenderedContrast * 0.85 : MinRenderedContrast;
            if (best < need)
                yield return new(Name(c), $"rendered text is not visible (strongest contrast {best:0.0}:1{(c.Enabled ? "" : ", disabled")})");
        }
    }

    /// <summary>Contrast of the strongest pixel against the most common colour (the background) in <paramref name="r"/>.</summary>
    private static double StrongestContrast(Bitmap bmp, Rectangle r)
    {
        // LockBits: GetPixel per pixel made a 60-row form's check take seconds
        var counts = new Dictionary<int, int>();
        var data = bmp.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[r.Width];
            for (int y = 0; y < r.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, r.Width);
                foreach (int argb in row) counts[argb] = counts.TryGetValue(argb, out int n) ? n + 1 : 1;
            }
        }
        finally { bmp.UnlockBits(data); }
        Color background = Color.FromArgb(counts.MaxBy(kv => kv.Value).Key);
        double best = 1;
        foreach (int argb in counts.Keys) best = Math.Max(best, Contrast(Color.FromArgb(argb), background));
        return best;
    }

    private static Color EffectiveBackColor(Control c)
    {
        for (Control? p = c; p is not null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return SystemColors.Control;
    }

    /// <summary>WCAG 2 contrast ratio of two colours (1 to 21).</summary>
    public static double Contrast(Color a, Color b) => AiFgfColor.Contrast(a, b);

    // ----------------------------------------------------------------- helpers

    private static string Name(Control c)
        => string.IsNullOrEmpty(c.Text) || c is TextBoxBase or ComboBox ? c.GetType().Name : $"{c.GetType().Name} '{c.Text}'";

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control d in Descendants(c)) yield return d;
        }
    }

    private static void Settle(Form form)
    {
        form.PerformLayout();
        Pump();
        form.PerformLayout();
    }

    private static void Pump()
    {
        for (int i = 0; i < 3; i++) Application.DoEvents();
    }

    private const int WM_DPICHANGED = 0x02E0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    /// <summary>
    /// Give <paramref name="form"/> this outer size, as it would be on a monitor at its <see cref="Control.DeviceDpi"/>.
    /// After <see cref="MoveToDpi"/> the window still sits on the system-DPI monitor, so Windows draws its frame
    /// and caption at the system DPI: at 144 DPI moved to 96 the frame is 22x56 px instead of 16x39, and the
    /// client area 17 px shorter than on a real 100 % monitor. The difference is added to the size, so the
    /// client area is what that monitor would give.
    /// </summary>
    public static void Resize(Form form, Size size)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.Size = size;
        if (!form.IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)) return;
        int real = (int)GetDpiForWindow(form.Handle);
        if (real <= 0 || real == form.DeviceDpi) return;
        Size frame = Frame(form, form.DeviceDpi), actual = new(form.Width - form.ClientSize.Width, form.Height - form.ClientSize.Height);
        Size extra = new(Math.Max(0, actual.Width - frame.Width), Math.Max(0, actual.Height - frame.Height));
        if (extra != Size.Empty) form.Size = new Size(size.Width + extra.Width, size.Height + extra.Height);
    }

    /// <summary>The frame and caption of the form's window at a DPI (outer minus client size).</summary>
    private static Size Frame(Form form, int dpi)
    {
        var r = new RECT();
        int style = GetWindowLong(form.Handle, GWL_STYLE), ex = GetWindowLong(form.Handle, GWL_EXSTYLE);
        if (!AdjustWindowRectExForDpi(ref r, style, false, ex, (uint)dpi)) return Size.Empty;
        return new Size(r.Right - r.Left, r.Bottom - r.Top);
    }

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(ref RECT rect, int style, [MarshalAs(UnmanagedType.Bool)] bool menu, int exStyle, uint dpi);

    /// <summary>Send the form what Windows sends when it moves to a monitor with <paramref name="dpi"/>.</summary>
    public static void MoveToDpi(Form form, int dpi)
    {
        double k = dpi / (double)form.DeviceDpi;
        var r = new RECT { Left = form.Left, Top = form.Top, Right = form.Left + (int)(form.Width * k), Bottom = form.Top + (int)(form.Height * k) };
        SendMessage(form.Handle, WM_DPICHANGED, (IntPtr)((dpi << 16) | dpi), ref r);
        Pump();
    }
}
