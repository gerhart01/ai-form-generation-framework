using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace AI_FGF.Avalonia;

/// <summary>A layout problem found by <see cref="AiFgfConformance"/>.</summary>
public sealed record ConformanceIssue(string Where, string Message)
{
    public override string ToString() => $"{Where}: {Message}";
}

/// <summary>
/// "Is this window laid out well?" — the Avalonia side of the WinForms <c>AiFgfConformance.CheckSpacing</c>, with the
/// same rules in logical pixels (Avalonia lays out in device-independent pixels, so no DPI scaling is needed).
/// </summary>
public static class AiFgfConformance
{
    /// <summary>
    /// Spacing between what the user sees, across containers, as drawn (window coordinates, clipped by every
    /// clipping ancestor): two items overlap; neighbours in a row or a column are closer than the minimum gap; a
    /// label or input is not centred on its row neighbour; the gaps of a button row are uneven; an item touches the
    /// window's edge. Limits: 4 px between controls, 2 next to a label, centres within 2 (a slider's thumb within 4),
    /// button gaps within 2, 3 from the window's left/top/right edge.
    /// </summary>
    public static IReadOnlyList<ConformanceIssue> CheckSpacing(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var items = Items(window).ToList();
        var clipped = items.Where(x => x.Clipped).Select(x => x.Control).ToHashSet();
        var issues = new List<ConformanceIssue>();

        for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
            {
                var (a, ra, _) = items[i];
                var (b, rb, _) = items[j];
                Rect o = ra.Intersect(rb);
                if (o.Width > 1 && o.Height > 1)
                {
                    if (a.GetVisualParent() != b.GetVisualParent()) issues.Add(new(Name(a), $"overlaps {Name(b)} (in another container) by {o.Width:0}x{o.Height:0}px"));
                    continue;
                }
                if (clipped.Contains(a) || clipped.Contains(b)) continue;   // partly scrolled away: its centre is not where it is drawn
                bool label = IsLabel(a) || IsLabel(b);
                double vOverlap = Math.Min(ra.Bottom, rb.Bottom) - Math.Max(ra.Top, rb.Top);
                double hOverlap = Math.Min(ra.Right, rb.Right) - Math.Max(ra.Left, rb.Left);
                if (vOverlap * 2 >= Math.Min(ra.Height, rb.Height))   // the same row
                {
                    var (l, rl, r, rr) = ra.Left <= rb.Left ? (a, ra, b, rb) : (b, rb, a, ra);
                    double gap = rr.Left - rl.Right;
                    double min = label ? 2 : 4;
                    if (gap >= -0.5 && gap < min - 0.5)
                        issues.Add(new(Name(l), $"is {gap:0.#}px from {Name(r)} — too close (at least {min}px)"));
                    if (gap >= -0.5 && (IsLabel(l) || gap < 40) && OneLine(l, rl) && OneLine(r, rr) && Nearest(items, rl, rr))
                    {
                        double dc = Math.Abs(Centre(l, rl, window) - Centre(r, rr, window));
                        // a caption wrapped onto more lines than its neighbour may instead line up at the top
                        bool topAligned = Wraps(l) != Wraps(r) && Math.Abs(rl.Top - rr.Top) <= 6;
                        double tolerance = l is Slider || r is Slider ? 4 : 2;
                        if (dc > tolerance + 0.5 && !topAligned)
                            issues.Add(new(Name(l), $"is not centred on {Name(r)} beside it (centres {dc:0.#}px apart; {rl.Top - rr.Top:+0.#;-0.#;0}px top, {rl.Height:0.#} vs {rr.Height:0.#}px high)"));
                    }
                }
                else if (hOverlap * 2 >= Math.Min(ra.Width, rb.Width) && !label)   // the same column
                {
                    double gap = Math.Max(ra.Top, rb.Top) - Math.Min(ra.Bottom, rb.Bottom);
                    if (gap >= -0.5 && gap < 1.5)
                        issues.Add(new(Name(ra.Top <= rb.Top ? a : b), $"is {gap:0.#}px from {Name(ra.Top <= rb.Top ? b : a)} below it — too close (at least 2px)"));
                }
            }

        // button rows: the same parent, the same centre line, gaps alike
        foreach (var row in items.Where(x => x.Control is Button and not ToggleButton)
                                 .GroupBy(x => (Parent: x.Control.GetVisualParent(), Centre: (int)((x.Bounds.Top + x.Bounds.Bottom) / 2 / 4))))
        {
            var sorted = row.OrderBy(x => x.Bounds.Left).ToList();
            if (sorted.Count < 3) continue;
            var gaps = sorted.Zip(sorted.Skip(1), (x, y) => y.Bounds.Left - x.Bounds.Right).Where(g => g >= 0).ToList();
            if (gaps.Count < 2) continue;
            double min = gaps.Min();
            var near = gaps.Where(g => g <= Math.Max(min * 3, 12)).ToList();   // a wide gap separates groups on purpose
            if (near.Count >= 2 && near.Max() - near.Min() > 2.5)
                issues.Add(new(Name(sorted[0].Control) + " row", $"uneven gaps between buttons: {string.Join(", ", gaps.Select(g => g.ToString("0.#")))}px"));
        }

        // the window's edges (the bottom has the status bar)
        Size client = window.ClientSize;
        foreach (var (c, r, _) in items)
            if (r.Left < 2.5 || client.Width - r.Right < 2.5 || r.Top < 2.5)
                issues.Add(new(Name(c), $"touches the window's edge ({r.Left:0.#}/{r.Top:0.#}/{client.Width - r.Right:0.#}px from left/top/right)"));
        return issues;
    }

    /// <summary>What the user sees as one thing — not a part of another's template — with its rectangle in the window clipped by every clipping ancestor.</summary>
    private static IEnumerable<(Control Control, Rect Bounds, bool Clipped)> Items(Window window)
    {
        foreach (Control c in window.GetVisualDescendants().OfType<Control>())
        {
            if (!IsItem(c) || !c.IsEffectivelyVisible || c.Opacity <= 0 || c.TemplatedParent is not null) continue;   // an invisible sizer (a slider value's widest text) is not seen
            if (c.GetVisualAncestors().Any(a => a is Control p && (IsItem(p) || p is TabItem or DataGrid or ScrollBar))) continue;
            if (c.TranslatePoint(default, window) is not { } at) continue;
            Rect whole = new(at, c.Bounds.Size), r = whole;
            foreach (Visual p in c.GetVisualAncestors())
            {
                if (p == window) break;
                if (p.ClipToBounds && p.TranslatePoint(default, window) is { } pa) r = r.Intersect(new Rect(pa, p.Bounds.Size));
            }
            r = r.Intersect(new Rect(window.ClientSize));
            if (r.Width > 2 && r.Height > 2) yield return (c, r, Math.Abs(r.Width - whole.Width) > 0.5 || Math.Abs(r.Height - whole.Height) > 0.5);
        }
    }

    private static bool IsItem(Control c) => c switch
    {
        SelectableTextBlock => false,   // the log
        TextBlock t => !string.IsNullOrWhiteSpace(t.Text),
        Label or Button or TextBox or ComboBox or NumericUpDown or Slider or ListBox or ProgressBar => true,
        _ => false,
    };

    private static bool IsLabel(Control c) => c is Label or TextBlock;

    private static double LineHeight(Control c) => c.GetValue(TextElement.FontSizeProperty) * 4 / 3;

    /// <summary>The text that shows a control's caption: itself, or the first text in its template.</summary>
    private static TextBlock? Text(Control c)
        => c as TextBlock ?? c.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text) && t.IsEffectivelyVisible);

    /// <summary>A caption on more than one line.</summary>
    private static bool Wraps(Control c)
        => (IsLabel(c) || c is Button) && Text(c) is { } t && t.TextLayout.TextLines.Count > 1;

    private static bool OneLine(Control c, Rect r)
        => c is Slider || c is not TextBox { AcceptsReturn: true } and not ListBox && r.Height < LineHeight(c) * 2;

    /// <summary>The centre line as drawn: a caption's text, a slider's thumb, otherwise the control.</summary>
    private static double Centre(Control c, Rect r, Window window)
    {
        Visual? part = c switch
        {
            Slider s => s.GetVisualDescendants().OfType<Thumb>().FirstOrDefault(),
            TextBlock => null,
            Label or Button => Text(c),
            _ => null,
        };
        if (part is not null && part.TranslatePoint(default, window) is { } p) return p.Y + part.Bounds.Height / 2;
        if (c is TextBlock t) return r.Top + t.Padding.Top + (r.Height - t.Padding.Top - t.Padding.Bottom) / 2;
        return (r.Top + r.Bottom) / 2;
    }

    /// <summary>Nothing between the two in the row.</summary>
    private static bool Nearest(List<(Control Control, Rect Bounds, bool Clipped)> items, Rect rl, Rect rr)
        => !items.Any(x => x.Bounds.Left >= rl.Right && x.Bounds.Right <= rr.Left && x.Bounds.Bottom > Math.Max(rl.Top, rr.Top) && x.Bounds.Top < Math.Min(rl.Bottom, rr.Bottom));

    private static string Name(Control c)
    {
        string text = c switch
        {
            TextBlock t => t.Text ?? "",
            ContentControl cc => AiFgf.Caption(cc.Content),
            _ => "",
        };
        if (text.Length > 30) text = text[..30] + "…";
        return text.Length == 0 ? c.GetType().Name : $"{c.GetType().Name} '{text}'";
    }
}
