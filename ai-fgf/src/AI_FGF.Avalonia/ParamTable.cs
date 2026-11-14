using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;

namespace AI_FGF.Avalonia;

/// <summary>
/// The labelled rows of a tool window, responsive like the WinForms <c>ParamTable</c>: label |
/// input | trailing button side by side while the input gets at least <see cref="MinInputEm"/> em,
/// stacked (label above, input below) when narrower — with an em of hysteresis so a window resized
/// at the breakpoint does not flicker. Rows whose input is hidden take no space. Everything is in
/// device-independent units, so the layout is the same at every DPI / scale factor.
/// </summary>
public sealed class ParamTable : Panel
{
    private sealed record Row(Control? Label, Control Input, Control? Trailing);

    private readonly List<Row> _rows = new();
    private bool _stacked;

    /// <summary>Stack when the input column would be narrower than this many em.</summary>
    public double MinInputEm { get; set; } = 16;

    /// <summary>Labels wider than this many em wrap onto more lines instead of widening the label column.</summary>
    public double MaxLabelEm { get; set; } = 22;

    /// <summary>Extra gap between columns and rows (0: the controls' WinForms margins space them, see <see cref="WinFormsMetrics"/>).</summary>
    public double Gap { get; set; }

    /// <summary>True while labels sit above their inputs.</summary>
    public bool IsStacked => _stacked;

    public void AddRow(Control? label, Control input, Control? trailing = null)
    {
        _rows.Add(new Row(label, input, trailing));
        // a button beside a field wraps its caption sooner: one 25 em wide made the window's minimum wider than the screen
        if (trailing is Button b) AiFgf.CapWidth(b, MaxTrailingEm);
        if (label is not null) Children.Add(label);
        Children.Add(input);
        if (trailing is not null) Children.Add(trailing);
        InvalidateMeasure();
    }

    private IEnumerable<Row> Shown => _rows.Where(r => r.Input.IsVisible);

    private double Em => TextElement.GetFontSize(this) * 1.33;

    private double LabelWidth => Shown.Where(r => r.Label is not null).Select(r => r.Label!.DesiredSize.Width).DefaultIfEmpty(0).Max();

    private double TrailingWidth => Shown.Where(r => r.Trailing is not null).Select(r => r.Trailing!.DesiredSize.Width).DefaultIfEmpty(0).Max();

    /// <summary>Below this width the rows stack.</summary>
    public double StackBreakpoint => LabelWidth + Gap + MinInputEm * Em + (TrailingWidth > 0 ? Gap + TrailingWidth : 0);

    /// <summary>The narrowest width at which the stacked layout still shows everything.</summary>
    public double MinimumContentWidth
        => Math.Max(LabelWidth, Shown.Select(r => (r.Input is WrapPanel ? r.Input.DesiredSize.Width : 8 * Em)
                                              + (r.Trailing is null ? 0 : Gap + r.Trailing.DesiredSize.Width)).DefaultIfEmpty(0).Max());

    protected override Size MeasureOverride(Size available)
    {
        // labels and trailing buttons at their natural size first: they decide the mode
        double maxLabel = MaxLabelEm * Em;
        foreach (Row r in _rows)
        {
            if (r.Label is not null && r.Label.MaxWidth != maxLabel) r.Label.MaxWidth = maxLabel;   // in em: follows the font
            r.Label?.Measure(Size.Infinity);
            r.Trailing?.Measure(Size.Infinity);
            if (!r.Input.IsVisible) r.Input.Measure(Size.Infinity);
        }
        double width = double.IsInfinity(available.Width) ? StackBreakpoint + 4 * Em : available.Width;
        _stacked = _stacked ? width < StackBreakpoint + Em : width < StackBreakpoint;

        double y = 0, natural = 0;
        foreach (Row r in Shown)
        {
            double trailing = r.Trailing is null ? 0 : r.Trailing.DesiredSize.Width + Gap;
            if (_stacked)
            {
                if (r.Label is not null) y += r.Label.DesiredSize.Height + Gap / 2;
                r.Input.Measure(new Size(Math.Max(0, width - trailing), double.PositiveInfinity));
                y += Math.Max(r.Input.DesiredSize.Height, r.Trailing?.DesiredSize.Height ?? 0) + Gap;
            }
            else
            {
                double inputW = Math.Max(0, width - LabelWidth - Gap - trailing);
                r.Input.Measure(new Size(inputW, double.PositiveInfinity));
                y += Offsets(r, inputW).Height + Gap;
            }
            natural = Math.Max(natural, LabelWidth + Gap + r.Input.DesiredSize.Width + trailing);
        }
        if (y > 0) y -= Gap;
        return new Size(double.IsInfinity(available.Width) ? natural : width, y);
    }

    /// <summary>
    /// Where the label and the input sit in a side-by-side row, and its height: both centred on the row — but a label
    /// beside a wrapping row (radio buttons) is centred on its first line, not between two of them.
    /// </summary>
    private static (double Label, double Input, double Height) Offsets(Row r, double inputWidth)
    {
        double ih = r.Input.DesiredSize.Height, lh = r.Label?.DesiredSize.Height ?? 0, th = r.Trailing?.DesiredSize.Height ?? 0;
        if (r.Label is { } label && r.Input is WrapPanel wrap && wrap.Children.Where(c => c.IsVisible).ToList() is { Count: > 0 } kids)
        {
            // the first line: the items that fit the width, centred on the tallest of them
            double room = inputWidth - wrap.Margin.Left - wrap.Margin.Right, x = 0, lineH = 0;
            foreach (Control c in kids)
            {
                if (x > 0 && x + c.DesiredSize.Width > room + 0.5) break;
                x += c.DesiredSize.Width; lineH = Math.Max(lineH, c.DesiredSize.Height);
            }
            Control first = kids[0];
            double line = wrap.Margin.Top + first.Margin.Top + (lineH - first.Margin.Top - first.Margin.Bottom) / 2;
            double text = label.Margin.Top + (lh - label.Margin.Top - label.Margin.Bottom) / 2;
            double labelY = Math.Max(0, line - text), inputY = Math.Max(0, text - line);
            return (labelY, inputY, Math.Max(Math.Max(labelY + lh, inputY + ih), th));
        }
        double h = Math.Max(ih, Math.Max(lh, th));
        return ((h - lh) / 2, (h - ih) / 2, h);
    }

    /// <summary>A button beside a field (Browse…) wraps its caption beyond this many em.</summary>
    public const double MaxTrailingEm = 12;

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0, labelW = LabelWidth;
        foreach (Row r in _rows.Where(r => !r.Input.IsVisible))
        {
            r.Label?.Arrange(default);
            r.Input.Arrange(default);
            r.Trailing?.Arrange(default);
        }
        foreach (Row r in Shown)
        {
            double trailingW = r.Trailing?.DesiredSize.Width ?? 0;
            double trailing = r.Trailing is null ? 0 : trailingW + Gap;
            if (_stacked)
            {
                if (r.Label is not null)
                {
                    r.Label.Arrange(new Rect(0, y, r.Label.DesiredSize.Width, r.Label.DesiredSize.Height));
                    y += r.Label.DesiredSize.Height + Gap / 2;
                }
                double h = Math.Max(r.Input.DesiredSize.Height, r.Trailing?.DesiredSize.Height ?? 0);
                double inputW = Math.Max(0, final.Width - trailing);
                r.Input.Arrange(new Rect(0, y + (h - r.Input.DesiredSize.Height) / 2, inputW, r.Input.DesiredSize.Height));
                r.Trailing?.Arrange(new Rect(final.Width - trailingW, y + (h - r.Trailing.DesiredSize.Height) / 2, trailingW, r.Trailing.DesiredSize.Height));
                y += h + Gap;
            }
            else
            {
                double x = labelW + Gap;   // a row without a label (the flags) lines up with the inputs
                double inputW = Math.Max(0, final.Width - x - trailing);
                var (labelY, inputY, h) = Offsets(r, inputW);
                r.Label?.Arrange(new Rect(0, y + labelY, labelW, r.Label.DesiredSize.Height));
                r.Input.Arrange(new Rect(x, y + inputY, inputW, r.Input.DesiredSize.Height));
                r.Trailing?.Arrange(new Rect(final.Width - trailingW, y + (h - r.Trailing.DesiredSize.Height) / 2, trailingW, r.Trailing.DesiredSize.Height));
                y += h + Gap;
            }
        }
        return final;
    }
}
