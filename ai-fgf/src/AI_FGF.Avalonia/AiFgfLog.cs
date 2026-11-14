using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DColor = System.Drawing.Color;

namespace AI_FGF.Avalonia;

/// <summary>
/// The output log — the same API as the WinForms <c>AiFgfLog</c> (<see cref="Line"/>, <see cref="Error"/>,
/// <see cref="Success"/>, <see cref="Warn"/>, <see cref="Append"/>, <see cref="Lines"/>,
/// <see cref="Clear"/>, <see cref="FocusFind"/>): monospace, lines never wrap, coloured lines,
/// selectable text, a Find bar (Find: / Next / Prev / Clear). Lines may come from any thread; they
/// are batched onto the UI thread, and the view follows the end unless the user scrolled up.
/// </summary>
public sealed class AiFgfLog : DockPanel
{
    private readonly SelectableTextBlock _text;
    private readonly ScrollViewer _scroll;
    private readonly TextBox _find;
    private readonly TextBlock _findInfo;
    private readonly ConcurrentQueue<(string Text, DColor? Color)> _pending = new();
    private readonly List<string> _lines = new();
    private int _scheduled, _matchLine = -1;

    /// <summary>Oldest lines are dropped beyond this many.</summary>
    public int MaxLines { get; set; } = 50_000;

    public AiFgfLog()
    {
        _text = new SelectableTextBlock
        {
            FontFamily = AvTheme.MonoFont,
            FontSize = 9.5 * 96 / 72,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(2, 1),   // a RichTextBox's text inset
            Inlines = new InlineCollection(),
        };
        AvTheme.Bind(_text, TextBlock.ForegroundProperty, AvTheme.LogForeKey);
        void Rescaled(object? s, EventArgs e) { if (s is TopLevel top) PitchLikeGdi(top); }
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not { } top) return;
            PitchLikeGdi(top);
            top.ScalingChanged += Rescaled;
        };
        DetachedFromVisualTree += (_, e) => { if (e.Root is TopLevel top) top.ScalingChanged -= Rescaled; };
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _text,
        };
        var page = AvTheme.Bind(new Border { Child = _scroll }, Border.BackgroundProperty, AvTheme.LogBackKey);

        _find = new TextBox { Margin = WinFormsMetrics.FindBoxMargin, VerticalAlignment = VerticalAlignment.Top };
        _find.TextChanged += (_, _) => FindNext(forward: true, fromStart: true);
        _find.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { FindNext(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; }
            else if (e.Key == Key.Escape && !string.IsNullOrEmpty(_find.Text)) { _find.Text = ""; e.Handled = true; }
        };
        double pad = WinFormsMetrics.TextPad(AvTheme.UiFontSize);
        _findInfo = AvTheme.Bind(new TextBlock { Margin = WinFormsMetrics.FindInfoMargin, Padding = new Thickness(pad, 0) }, TextBlock.ForegroundProperty, AvTheme.SubtleKey);
        // the WinForms find bar (AiFgf.CentreRows): every control centred on the tallest, side margins kept
        var findBar = new FindBar(_find) { Margin = WinFormsMetrics.FindBarMargin };
        findBar.Children.Add(new TextBlock { Text = AiFgfText.Find, Margin = WinFormsMetrics.FindLabelMargin, Padding = new Thickness(pad, 0) });
        findBar.Children.Add(_find);
        findBar.Children.Add(AiFgf.SmallButton(AiFgfText.FindNext, (_, _) => FindNext(forward: true)));
        findBar.Children.Add(AiFgf.SmallButton(AiFgfText.FindPrevious, (_, _) => FindNext(forward: false)));
        findBar.Children.Add(AiFgf.SmallButton(AiFgfText.ClearLog, (_, _) => Clear()));
        findBar.Children.Add(_findInfo);

        SetDock(findBar, Dock.Top);
        Children.Add(findBar);
        Children.Add(page);
    }

    /// <summary>The WinForms log's character pitch (whole device pixels) at the window's scale.</summary>
    private void PitchLikeGdi(TopLevel top) => _text.LetterSpacing = WinFormsMetrics.GdiMonoSpacing(_text.FontFamily, _text.FontSize, top.RenderScaling);

    public void Line(string text) => Append(text, null);
    public void Error(string text) => Append(text, AiFgfTheme.Current.LogError);
    public void Success(string text) => Append(text, AiFgfTheme.Current.LogSuccess);
    public void Warn(string text) => Append(text, AiFgfTheme.Current.LogWarn);

    /// <summary>Append a line in a colour (null = the log's text colour); any thread.</summary>
    public void Append(string text, DColor? color)
    {
        _pending.Enqueue((text, color));
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
    }

    /// <summary>The lines written so far (pending ones are flushed first when called on the UI thread).</summary>
    public string[] Lines
    {
        get
        {
            if (Dispatcher.UIThread.CheckAccess()) Flush();
            return _lines.ToArray();
        }
    }

    public int LineCount => Lines.Length;

    public void Clear()
    {
        while (_pending.TryDequeue(out _)) { }
        _lines.Clear();
        _text.Inlines!.Clear();
        _matchLine = -1;
        _findInfo.Text = "";
    }

    /// <summary>Put the caret in the find box (Ctrl+F).</summary>
    public void FocusFind() { _find.Focus(); _find.SelectAll(); }

    /// <summary>Write pending lines now (UI thread).</summary>
    public void Flush()
    {
        Interlocked.Exchange(ref _scheduled, 0);
        if (_pending.IsEmpty) return;
        bool atEnd = _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 4;
        InlineCollection inlines = _text.Inlines!;
        while (_pending.TryDequeue(out var item))
        {
            var run = new Run(item.Text);
            if (item.Color is { } c) run.Foreground = AvTheme.Brush(c);
            inlines.Add(run);
            inlines.Add(new LineBreak());
            _lines.Add(item.Text);
        }
        int excess = _lines.Count - MaxLines;
        if (excess > 0)
        {
            _lines.RemoveRange(0, excess);
            for (int i = 0; i < excess * 2 && inlines.Count > 0; i++) inlines.RemoveAt(0);
        }
        if (atEnd) Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void FindNext(bool forward, bool fromStart = false)
    {
        Flush();
        string term = _find.Text ?? "";
        if (term.Length == 0) { _findInfo.Text = ""; _text.SelectionStart = _text.SelectionEnd = 0; return; }
        int n = _lines.Count, start = fromStart ? (forward ? -1 : n) : _matchLine;
        for (int k = 1; k <= n; k++)
        {
            int i = ((start + (forward ? k : -k)) % n + n) % n;
            int col = _lines[i].IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (col < 0) continue;
            _matchLine = i;
            int offset = 0;
            for (int j = 0; j < i; j++) offset += _lines[j].Length + 1;   // each line + its break
            _text.SelectionStart = offset + col;
            _text.SelectionEnd = offset + col + term.Length;
            double lineH = _text.FontSize * 1.3;
            _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, i * lineH - _scroll.Viewport.Height / 2));
            _findInfo.Text = "";
            return;
        }
        _findInfo.Text = AiFgfText.NoMatches;
    }
}

/// <summary>
/// The find bar's layout in one pass: the find box takes what the other items leave (80–260 px),
/// so the bar stays on one row as long as it can; only when even the narrowest box does not fit do
/// items flow onto further rows. (Resizing the box from a SizeChanged handler raced the layout.) Each row is
/// centred on its tallest control with 3 px above and below, as the WinForms bar (AiFgf.CentreRows).
/// </summary>
internal sealed class FindBar : Panel
{
    private readonly Control _box;
    private const double MinBox = 80, MaxBox = 260, Gap = 3;

    public FindBar(Control box) => _box = box;

    private double BoxWidth(double available)
    {
        double others = Children.Where(c => c != _box && c.IsVisible).Sum(c => c.DesiredSize.Width);
        return Math.Clamp(available - others - _box.Margin.Left - _box.Margin.Right, MinBox, MaxBox);
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (Control c in Children.Where(c => c != _box)) c.Measure(Size.Infinity);
        double width = double.IsInfinity(available.Width) ? 2000 : available.Width;
        _box.Measure(new Size(BoxWidth(width) + _box.Margin.Left + _box.Margin.Right, double.PositiveInfinity));
        return Flow(width, arrange: false);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Flow(final.Width, arrange: true);
        return final;
    }

    private Size Flow(double width, bool arrange)
    {
        // vertical margins are replaced by Gap above and below the row's tallest control; each is centred on it
        static double Inner(Control c) => c.DesiredSize.Height - c.Margin.Top - c.Margin.Bottom;
        double x = 0, y = 0, rowH = 0, widest = 0;
        double box = BoxWidth(width) + _box.Margin.Left + _box.Margin.Right;
        var row = new List<(Control C, double X, double W)>();
        void EndRow()
        {
            if (arrange)
                foreach (var (c, cx, cw) in row)
                {
                    double top = y + Gap + (rowH - Inner(c)) / 2;
                    c.Arrange(new Rect(cx, top - c.Margin.Top, Math.Min(cw, width), Inner(c) + c.Margin.Top + c.Margin.Bottom));
                }
            if (row.Count > 0) y += rowH + 2 * Gap;
            row.Clear(); x = 0; rowH = 0;
        }
        foreach (Control c in Children.Where(c => c.IsVisible))
        {
            double w = c == _box ? box : c.DesiredSize.Width;
            if (x > 0 && x + w > width + 0.5) EndRow();
            row.Add((c, x, w));
            x += w; rowH = Math.Max(rowH, Inner(c)); widest = Math.Max(widest, x);
        }
        EndRow();
        return new Size(Math.Min(widest, width), y);
    }
}
