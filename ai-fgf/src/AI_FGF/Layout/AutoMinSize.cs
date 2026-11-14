using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>How a set of buttons is arranged, for computing the space they need.</summary>
public enum ButtonLayout
{
    /// <summary>A wrapping bar (the AI_FGF default): the minimum width is one button wide.</summary>
    WrapRow,
    /// <summary>A single non-wrapping row: the minimum width is every button side by side.</summary>
    SingleRow,
    /// <summary>A vertical stack: the minimum height is every button top to bottom.</summary>
    VerticalStack,
    /// <summary>A fixed-column grid: the minimum width is columns × a button's minimum width.</summary>
    Grid,
}

/// <summary>
/// Computes and applies a sensible <see cref="Form.MinimumSize"/> so a form's buttons never
/// overlap or clip when it is resized — the runtime companion to the placement-threshold
/// report. It measures the actual buttons at the current DPI (so it is correct for any
/// labels, fonts and scale), guards the width against the button bar and the height against
/// the stack of docked panels, and re-applies itself on a DPI change.
///
/// <code>
/// AutoMinSize.Guard(form, buttonBar);     // keep the form from shrinking past its buttons
/// // …add or relabel buttons later…
/// AutoMinSize.Refit(form);                 // recompute on demand
/// </code>
/// </summary>
public static partial class AutoMinSize
{
    /// <summary>
    /// The largest minimum size, in logical (96-DPI) pixels, a form may ask for: the working area
    /// of 1280x720 at 125 % (1024x576) minus a 48-px taskbar — the smallest screen Windows offers at
    /// any scale. A form whose parameters need more height gets scrolling parameters instead.
    /// </summary>
    public static Size SmallestScreen { get; set; } = new(1024, 528);

    private sealed record Config(FlowLayoutPanel? Bar, int MinBodyWidth, int MinBodyHeight, bool Placed = false);

    private static readonly ConditionalWeakTable<Form, Config> Configs = new();

    /// <summary>
    /// Keep <paramref name="form"/> from being resized small enough for its buttons to overlap.
    /// If <paramref name="bar"/> is given, the width is guarded against that wrapping button bar.
    /// The height is guarded so the docked Top/Bottom panels plus a minimum body always fit.
    /// Re-applies automatically on DPI changes.
    /// </summary>
    public static void Guard(Form form, FlowLayoutPanel? bar = null, int minBodyWidth = 360, int minBodyHeight = 180)
    {
        ArgumentNullException.ThrowIfNull(form);
        Configs.AddOrUpdate(form, new Config(bar, minBodyWidth, minBodyHeight));
        form.DpiChanged -= OnDpiChanged;
        form.DpiChanged += OnDpiChanged;
        // Re-apply once the form is shown: only then are the non-client borders and the docked
        // panel heights known, so a Guard called from the constructor becomes accurate.
        form.Shown -= OnShown;
        form.Shown += OnShown;
        form.Resize -= OnFormResize;
        form.Resize += OnFormResize;
        // Re-apply when buttons are added to or removed from the bar at runtime, so the minimum
        // size tracks the current button set (a wider button widens the floor, and so on).
        // Rows shown/hidden by a condition change the height the parameters need.
        foreach (ParamTable table in Descendants(form).OfType<ParamTable>())
        {
            table.RowsChanged -= OnRowsChanged;
            table.RowsChanged += OnRowsChanged;
        }
        if (bar is not null)
        {
            bar.ControlAdded -= OnBarChanged;
            bar.ControlAdded += OnBarChanged;
            bar.ControlRemoved -= OnBarChanged;
            bar.ControlRemoved += OnBarChanged;
        }
        Apply(form);
    }

    /// <summary>Recompute and re-apply the minimum size now (e.g. after adding or relabelling buttons).</summary>
    public static void Refit(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        Apply(form);
    }

    private static void OnDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        // DpiChanged fires *before* WinForms rescales the form (fonts, bounds, MinimumSize):
        // measuring now would mix new-DPI units with old-DPI controls, and the framework would
        // then scale that MinimumSize a second time. Re-measure once the rescale is done.
        if (sender is Form f && f.IsHandleCreated && !f.IsDisposed)
            f.BeginInvoke(() => { if (!f.IsDisposed) Apply(f); });
    }

    private static void OnShown(object? sender, EventArgs e)
    {
        if (sender is Form f) Apply(f);
    }

    private static void OnRowsChanged(object? sender, EventArgs e)
    {
        if (sender is Control c && c.FindForm() is { } f) Apply(f);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control d in Descendants(c)) yield return d;
        }
    }

    private static void OnBarChanged(object? sender, ControlEventArgs e)
    {
        if (sender is Control c && c.FindForm() is { } f) Apply(f);
    }

    private static void Apply(Form form)
    {
        if (!Configs.TryGetValue(form, out Config? c) || c is null) return;

        int padH = form.Padding.Horizontal;
        var docked = form.Controls.Cast<Control>()
            .Where(ctl => ctl.Visible && ctl.Dock is DockStyle.Top or DockStyle.Bottom)
            .ToList();

        // Width: the narrowest each docked panel can *reflow* to — a wrapping bar is one button
        // wide, a responsive parameters table is its stacked minimum. (Using PreferredSize here
        // would be the unwrapped single-row width and defeat wrapping entirely.)
        int needClientW = form.LogicalToDeviceUnits(c.MinBodyWidth) + padH;
        if (c.Bar is not null) needClientW = Math.Max(needClientW, ButtonBarMinWidth(c.Bar) + padH);
        foreach (Control ctl in docked) needClientW = Math.Max(needClientW, MinWidthOf(ctl) + padH);

        // Height: what the docked stack needs *at that minimum width* (bars wrap into more rows,
        // parameter rows stack), plus a minimum body — so the stack can never collapse into itself.
        int minBody = form.LogicalToDeviceUnits(c.MinBodyHeight);
        // the body at least as tall as a log in it needs at that width (its find bar wraps as the window narrows)
        int HeightFor(int clientW) => docked.Sum(ctl => HeightAt(ctl, clientW - padH)) + Math.Max(minBody, LogNeed(form, clientW - padH)) + form.Padding.Vertical;
        int needClientH = HeightFor(needClientW);

        // Controls placed by coordinates and anchors: as small as they allow, never smaller than their docked company
        if (c.Placed)
        {
            Size placed = PlacedMinClient(form);
            needClientW = Math.Max(needClientW, placed.Width);
            needClientH = Math.Max(needClientH, placed.Height);
        }

        int ncW = Math.Max(0, form.Width - form.ClientSize.Width);
        int ncH = Math.Max(0, form.Height - form.ClientSize.Height);
        int budgetClientH = form.LogicalToDeviceUnits(SmallestScreen.Height) - ncH;
        int budgetClientW = form.LogicalToDeviceUnits(SmallestScreen.Width) - ncW;

        // Too tall for the smallest screen at the narrowest width (a bar wrapped one button per row,
        // stacked parameter rows)? A wider minimum trades width, which every screen has, for height.
        if (needClientH > budgetClientH)
        {
            int step = Math.Max(4, form.LogicalToDeviceUnits(8));
            int bestW = needClientW, bestH = needClientH;
            for (int w = needClientW + step; w <= budgetClientW; w += step)
            {
                int h = HeightFor(w);
                if (h < bestH) { bestW = w; bestH = h; }
                if (h <= budgetClientH) break;
            }
            needClientW = bestW;
            needClientH = bestH;
        }
        int innerW = needClientW - padH;

        // Still too tall? Let the parameters scroll: they may shrink to a couple of rows.
        if (needClientH > budgetClientH && docked.FirstOrDefault(IsScrollingParams) is { } paramsBox)
        {
            int full = HeightAt(paramsBox, innerW);
            int shrinkable = Math.Max(0, full - MinScrollingParamsHeight(paramsBox));
            needClientH = Math.Max(needClientH - shrinkable, Math.Min(needClientH, budgetClientH));
        }

        // Still too tall — a bar of very many buttons? Cap the bar (two rows at least) and let it
        // scroll: every button stays reachable, and the window fits the screen.
        if (needClientH > budgetClientH && c.Bar is { } bar)
        {
            int barH = HeightAt(bar, innerW);
            int allowed = Math.Max(MinBarHeight(bar, barH), barH - (needClientH - budgetClientH));
            if (allowed < barH) needClientH -= barH - allowed;
        }
        form.MinimumSize = new Size(needClientW + ncW, needClientH + ncH);
        FitBar(form, c);
        FitParams(form, c);
    }

    /// <summary>
    /// The height the filling body needs for the logs in it at this inner width: what surrounds a log there now
    /// (tab headers, borders), plus <see cref="AiFgfLog.MinimumHeightAt"/>. 0 when there is none, or before layout.
    /// </summary>
    private static int LogNeed(Form form, int innerW)
    {
        Control? fill = form.Controls.Cast<Control>().FirstOrDefault(ctl => ctl.Visible && ctl.Dock == DockStyle.Fill);
        if (fill is null || fill.Height <= 0) return 0;
        int need = 0;
        foreach (AiFgfLog log in Descendants(fill).OfType<AiFgfLog>())
        {
            if (log.Width <= 0 || log.Height <= 0) continue;
            int chromeW = fill.Width - log.Width, chromeH = fill.Height - log.Height;
            need = Math.Max(need, chromeH + log.MinimumHeightAt(innerW - chromeW));
        }
        return need;
    }

    private static bool IsScrollingParams(Control ctl) => ctl.Controls.OfType<ParamScroller>().Any();

    private static ParamTable? TableIn(Control ctl)
        => ctl.Controls.OfType<ParamTable>().FirstOrDefault()
           ?? ctl.Controls.OfType<ParamScroller>().SelectMany(p => p.Controls.OfType<ParamTable>()).FirstOrDefault();

    /// <summary>The least height scrolling parameters may take: their chrome plus about two rows.</summary>
    private static int MinScrollingParamsHeight(Control paramsBox)
        => (paramsBox.Height - paramsBox.DisplayRectangle.Height) + paramsBox.Font.Height * 4;

    private static bool _fitting;

    /// <summary>
    /// Cap the parameters' height to what the window has left after the other docked panels and the
    /// minimum body, so a short window scrolls the rows instead of cutting them (or the body) off.
    /// </summary>
    private static void FitParams(Form form, Config c)
    {
        if (_fitting) return;
        Control? paramsBox = form.Controls.Cast<Control>().FirstOrDefault(ctl => ctl.Visible && ctl.Dock == DockStyle.Top && IsScrollingParams(ctl));
        if (paramsBox is null) return;
        _fitting = true;
        try
        {
            // a wrapping bar by the height it is about to take: just uncapped, it still had its capped height, the
            // parameters took the difference and the body was squeezed to nothing (back from the minimum size)
            int others = form.Controls.Cast<Control>()
                .Where(ctl => ctl != paramsBox && ctl.Visible && ctl.Dock is DockStyle.Top or DockStyle.Bottom)
                .Sum(ctl => ctl is FlowLayoutPanel { WrapContents: true, AutoSize: true } flow && flow.Width > 0
                    ? Math.Min(ParamTable.WrappedHeight(flow, flow.Width), flow.MaximumSize.Height > 0 ? flow.MaximumSize.Height : int.MaxValue)
                    : ctl.Height);
            int available = form.ClientSize.Height - form.Padding.Vertical - others - Math.Max(form.LogicalToDeviceUnits(c.MinBodyHeight), LogNeed(form, form.ClientSize.Width - form.Padding.Horizontal));
            int full = HeightAt(paramsBox, paramsBox.Width);
            int cap = Math.Max(MinScrollingParamsHeight(paramsBox), available);
            bool capped = full > cap;
            Size max = capped ? new Size(0, cap) : Size.Empty;
            // Scroll only while capped: an always-on AutoScroll panel flashes a scroll bar during a
            // DPI rescale (new font, old size), and the narrower table would flip its layout.
            ParamScroller scroller = paramsBox.Controls.OfType<ParamScroller>().First();
            if (scroller.AutoScroll != capped) scroller.AutoScroll = capped;
            if (paramsBox.MaximumSize != max) paramsBox.MaximumSize = max;
        }
        finally { _fitting = false; }
    }

    private static void OnFormResize(object? sender, EventArgs e)
    {
        if (sender is Form f && Configs.TryGetValue(f, out Config? c) && c is not null && f.WindowState != FormWindowState.Minimized)
        {
            FitBar(f, c);
            FitParams(f, c);
        }
    }

    /// <summary>One row of buttons: the least a capped (scrolling) bar keeps.</summary>
    private static int MinBarHeight(FlowLayoutPanel bar, int full)
    {
        int row = bar.Controls.OfType<Control>().Where(b => b.Visible)
                     .Select(b => Math.Max(b.MinimumSize.Height, b.GetPreferredSize(Size.Empty).Height) + b.Margin.Vertical).DefaultIfEmpty(0).Max();
        return Math.Min(full, row + bar.Padding.Vertical);
    }

    private static bool _fittingBar;

    /// <summary>
    /// A bar of so many buttons that, wrapped, it leaves no room for the rest gets a capped height
    /// and scrolls (every button stays reachable); with room enough it shows all its rows again.
    /// </summary>
    private static void FitBar(Form form, Config c)
    {
        if (c.Bar is not { } bar || _fittingBar) return;
        _fittingBar = true;
        try
        {
            int width = bar.Width > 0 ? bar.Width : form.ClientSize.Width - form.Padding.Horizontal;
            int full = ParamTable.WrappedHeight(bar, width);
            int others = form.Controls.Cast<Control>()
                .Where(ctl => ctl != bar && ctl.Visible && ctl.Dock is DockStyle.Top or DockStyle.Bottom)
                .Sum(ctl => IsScrollingParams(ctl) ? Math.Min(ctl.Height, MinScrollingParamsHeight(ctl)) : ctl.Height);
            int available = form.ClientSize.Height - form.Padding.Vertical - others - Math.Max(form.LogicalToDeviceUnits(c.MinBodyHeight), LogNeed(form, form.ClientSize.Width - form.Padding.Horizontal));
            int cap = Math.Max(MinBarHeight(bar, full), available);
            Size max = cap < full ? new Size(0, cap) : Size.Empty;
            bool scroll = max != Size.Empty;
            if (bar.AutoScroll != scroll) bar.AutoScroll = scroll;
            if (bar.MaximumSize != max) bar.MaximumSize = max;
        }
        finally { _fittingBar = false; }
    }

    /// <summary>The narrowest width a docked panel can reflow to without clipping.</summary>
    private static int MinWidthOf(Control ctl)
    {
        switch (ctl)
        {
            case FlowLayoutPanel { WrapContents: true } flow:
                return flow.Controls.Cast<Control>().Where(x => x.Visible)
                           .Select(x => Math.Max(x.MinimumSize.Width, x.GetPreferredSize(Size.Empty).Width) + x.Margin.Horizontal)
                           .DefaultIfEmpty(0).Max() + flow.Padding.Horizontal;
            case StatusStrip:
                return 0; // a status strip truncates its text gracefully
        }
        if (TableIn(ctl) is { } table)
            return table.MinimumContentWidth + (ctl.Width - ctl.DisplayRectangle.Width) + SystemInformation.GetVerticalScrollBarWidthForDpi(ctl.DeviceDpi);
        return ctl.MinimumSize.Width;
    }

    /// <summary>The height a docked panel will take when the form is <paramref name="width"/> wide.</summary>
    private static int HeightAt(Control ctl, int width)
    {
        if (ctl is FlowLayoutPanel { WrapContents: true, AutoSize: true } flow)
            return ParamTable.WrappedHeight(flow, width);
        if (ctl.AutoSize && TableIn(ctl) is { } table)
        {
            int chromeW = ctl.Width - ctl.DisplayRectangle.Width;
            int chromeH = ctl.Height - ctl.DisplayRectangle.Height;
            return chromeH + table.HeightAt(width - chromeW);
        }
        return ctl.Height;
    }

    /// <summary>The minimum width a wrapping button bar needs (one button wide + its padding).</summary>
    public static int ButtonBarMinWidth(FlowLayoutPanel bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return ButtonsExtent(bar.Controls.OfType<Button>(), ButtonLayout.WrapRow, bar.Padding);
    }

    /// <summary>
    /// The minimum container extent (width for horizontal layouts, height for a vertical stack)
    /// to place these buttons without overlap or clipping, measured at their current DPI size.
    /// </summary>
    public static int ButtonsExtent(IEnumerable<Button> buttons, ButtonLayout layout, Padding containerPadding, int gridColumns = 4)
    {
        var bs = buttons.Where(b => b.Visible).ToList();
        if (bs.Count == 0) return layout == ButtonLayout.VerticalStack ? containerPadding.Vertical : containerPadding.Horizontal;

        int W(Button b) => Math.Max(b.MinimumSize.Width, b.GetPreferredSize(Size.Empty).Width) + b.Margin.Horizontal;
        int H(Button b) => Math.Max(b.MinimumSize.Height, b.GetPreferredSize(Size.Empty).Height) + b.Margin.Vertical;

        return layout switch
        {
            ButtonLayout.WrapRow => bs.Max(W) + containerPadding.Horizontal,
            ButtonLayout.SingleRow => bs.Sum(W) + containerPadding.Horizontal,
            ButtonLayout.VerticalStack => bs.Sum(H) + containerPadding.Vertical,
            ButtonLayout.Grid => Math.Min(gridColumns, bs.Count) == 0 ? containerPadding.Horizontal
                : gridColumns * (bs.Max(b => Math.Max(b.MinimumSize.Width, 1)) + bs.Max(b => b.Margin.Horizontal)) + containerPadding.Horizontal,
            _ => bs.Max(W) + containerPadding.Horizontal,
        };
    }
}

/// <summary>
/// The panel between the parameters group and its <see cref="ParamTable"/>. It scrolls only while
/// <see cref="AutoMinSize"/> caps the parameters' height (a window too short for all rows).
/// </summary>
internal sealed class ParamScroller : Panel
{
    public ParamScroller()
    {
        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }
}
