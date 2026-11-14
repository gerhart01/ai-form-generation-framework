using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// A multi-line text field (for input or, with <see cref="TextBoxBase.ReadOnly"/>, for showing
/// text) that never wraps — each line stays one line, as written — and avoids scrolling:
/// <list type="bullet">
/// <item>its height follows the text: at least <see cref="VisibleLines"/> lines, growing with each
/// line up to <see cref="MaxLines"/>; the window grows with it while the screen has room;</item>
/// <item>when text is set from code (settings, a preset, a result to show) the window widens to fit
/// the longest line, up to the screen's working area (<see cref="FitWindowToText"/>);</item>
/// <item>scroll bars appear only when the text still does not fit — beyond <see cref="MaxLines"/>
/// lines, or a line wider than the screen — instead of an empty bar always taking a line's height.</item>
/// </list>
/// The sizes follow DPI and font changes; it opens at the start of its text.
/// </summary>
public sealed class AiFgfMemo : TextBox
{
    private int _visibleLines = 4, _maxLines = 12;
    private bool _updating, _recreating, _fromCode, _hBar;
    private Size _pendingGrow;   // a window growth already posted (at most one in flight)
    private int _widest;   // px, the longest line as measured

    public AiFgfMemo()
    {
        Multiline = true;
        WordWrap = false;
        ScrollBars = ScrollBars.None;
        AcceptsReturn = true;
        AcceptsTab = false;          // Tab still moves focus
        BorderStyle = BorderStyle.FixedSingle;
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        Margin = new Padding(3, 4, 3, 4);   // symmetric top/bottom so the field centres on its label at every DPI
        BackColor = AiFgfTheme.Current.Surface;
        ForeColor = AiFgfTheme.Current.Text;
        UpdateLayout();
    }

    /// <summary>The fewest lines shown (1–100); the field never gets shorter than this.</summary>
    [DefaultValue(4)]
    public int VisibleLines
    {
        get => _visibleLines;
        set { _visibleLines = Math.Clamp(value, 1, 100); _maxLines = Math.Max(_maxLines, _visibleLines); UpdateLayout(); }
    }

    /// <summary>The most lines shown before a vertical scroll bar appears (≥ <see cref="VisibleLines"/>).</summary>
    [DefaultValue(12)]
    public int MaxLines
    {
        get => _maxLines;
        set { _maxLines = Math.Clamp(value, _visibleLines, 1000); UpdateLayout(); }
    }

    /// <summary>Widen (and heighten) the window to show the text without scrolling, within the screen. Default on.</summary>
    [DefaultValue(true)]
    public bool FitWindowToText { get; set; } = true;

    /// <summary>The number of text lines (at least 1).</summary>
    public int LineCount
    {
        get
        {
            // no wrapping: visual lines = text lines (counted on the string; no window handle needed)
            string t = Text;
            int n = 1;
            foreach (char ch in t) if (ch == '\n') n++;
            return n;
        }
    }

    /// <summary>How many lines the field shows now: the line count, kept within VisibleLines..MaxLines.</summary>
    public int ShownLines => Math.Clamp(LineCount, _visibleLines, _maxLines);

    private int Borders => BorderStyle == BorderStyle.None ? 0 : SystemInformation.BorderSize.Height * 2 + 2;
    private int Inset => LogicalToDeviceUnits(4);   // the edit control's text inset

    /// <summary>The height that shows <paramref name="lines"/> full lines (plus a horizontal scroll bar if asked).</summary>
    public int HeightForLines(int lines, bool horizontalBar = false)
        => lines * Font.Height + Borders + Inset + (horizontalBar ? SystemInformation.GetHorizontalScrollBarHeightForDpi(DeviceDpi) : 0);

    /// <summary>The height this field wants right now (its line count times the line height, plus
    /// borders and a horizontal scroll bar if one is shown) - read by <see cref="ParamTable"/>'s
    /// row-height fix the same way it already reads <see cref="ComboBox.PreferredHeight"/>.</summary>
    public new int PreferredHeight => HeightForLines(ShownLines, _hBar);

    /// <summary>The width the text area needs for the longest line.</summary>
    public int TextWidth => _widest + LogicalToDeviceUnits(8);   // margins + caret

    private int TextAreaWidth(bool verticalBar)
        => Width - Borders - (verticalBar ? SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi) : 0);

    /// <summary>True when the longest line is wider than the field (a horizontal scroll bar is shown).</summary>
    public bool HasLongLines => TextWidth > TextAreaWidth(LineCount > _maxLines);

    private void Measure()
    {
        int widest = 0;
        string[] lines = Lines;
        if (lines.Length > 5000) { _widest = int.MaxValue / 4; return; }   // huge text: don't measure, just scroll
        foreach (string line in lines)
            widest = Math.Max(widest, TextRenderer.MeasureText(line, Font, Size.Empty,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.ExpandTabs | TextFormatFlags.NoPadding).Width);
        _widest = widest;
    }

    /// <summary>Height, minimum height and scroll bars for the current text, width and font.</summary>
    private void UpdateLayout()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            bool needV = LineCount > _maxLines;
            // A table mid-layout can hand the field a transient width of a pixel or two; deciding on
            // it would add a bar, change the height, re-run the layout and loop. Keep the last decision.
            bool needH = _hBar;
            if (Width >= Font.Height * 4)
            {
                // Hysteresis: once shown, the horizontal bar goes only when the text fits with a scroll
                // bar's width to spare. Otherwise a container that gains or loses a vertical scroll bar
                // as this memo changes height would flip it on and off for ever.
                int slack = _hBar ? SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi) : 0;
                needH = TextWidth > TextAreaWidth(needV) - slack;
            }
            _hBar = needH;
            ScrollBars want = needH && needV ? ScrollBars.Both : needH ? ScrollBars.Horizontal : needV ? ScrollBars.Vertical : ScrollBars.None;
            if (ScrollBars != want)
            {
                // changing the style recreates the window: keep the caret, the view's start and the focus
                // (recreating a handle - unlike a Docked/AutoSize child reflowing - drops focus to whatever
                // the OS gives it next, same failure mode as the row-height staleness this file's OnLayout
                // callers already guard against, one layer lower: at the control, not the container)
                int start = SelectionStart, length = SelectionLength;
                bool hadFocus = Focused;
                _recreating = true;
                try { ScrollBars = want; }
                finally { _recreating = false; }
                if (IsHandleCreated)
                {
                    SelectionStart = Math.Min(start, TextLength); SelectionLength = length;
                    if (hadFocus) Focus();
                }
            }
            int h = HeightForLines(ShownLines, needH);
            int grew = h - Height;
            MinimumSize = new Size(0, h);
            if (Height != h) Height = h;
            if (grew > 0) GrowWindow(0, grew);
        }
        finally { _updating = false; }
    }

    /// <summary>Grow the window by up to <paramref name="dx"/> x <paramref name="dy"/>, staying on its screen.</summary>
    private void GrowWindow(int dx, int dy)
    {
        if (!FitWindowToText || FindForm() is not { } form || !form.Visible || form.WindowState != FormWindowState.Normal) return;
        if (dx <= 0 && dy <= 0) return;
        Rectangle work = Screen.FromControl(form).WorkingArea;
        int w = Math.Min(form.Width + Math.Max(0, dx), Math.Max(form.Width, work.Width));
        int h = Math.Min(form.Height + Math.Max(0, dy), Math.Max(form.Height, work.Height));
        if (w == form.Width && h == form.Height) return;
        bool posted = _pendingGrow != Size.Empty;
        _pendingGrow = new Size(Math.Max(_pendingGrow.Width, w), Math.Max(_pendingGrow.Height, h));
        if (posted) return;   // one growth in flight is enough; it takes the largest request
        form.BeginInvoke(() =>
        {
            (w, h) = (_pendingGrow.Width, _pendingGrow.Height);
            _pendingGrow = Size.Empty;
            if (form.IsDisposed) return;
            var b = new Rectangle(form.Left, form.Top, w, h);
            // keep it on the screen it is on (an off-screen form, as in tests, is only resized)
            if (work.IntersectsWith(form.Bounds))
            {
                if (b.Right > work.Right) b.X = Math.Max(work.Left, work.Right - b.Width);
                if (b.Bottom > work.Bottom) b.Y = Math.Max(work.Top, work.Bottom - b.Height);
            }
            form.Bounds = b;
        });
    }

    /// <summary>Widen the window so the longest line fits without a horizontal scroll bar (within the screen).</summary>
    public void FitWindowWidth()
    {
        int extra = TextWidth - TextAreaWidth(LineCount > _maxLines);
        // plus a scroll bar's width: room for a container's bar, and past the bar's hysteresis
        if (extra > 0) GrowWindow(extra + SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi), 0);
    }

    /// <summary>Text set from code (not typed): the memo opens at its start and the window fits it.</summary>
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            _fromCode = true;
            try { base.Text = value; }
            finally { _fromCode = false; }
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Measure();
        UpdateLayout();
        if (_fromCode)
        {
            ShowStart();      // a value set from code (settings, preset, result) opens at its start
            FitWindowWidth(); // … and the window widens to show it whole, if the screen allows
        }
    }

    private int _lastWidth = -1;
    private static readonly HashSet<AiFgfMemo> Dirty = new();
    private static bool _batchPosted;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        // Only a width change can change the scroll bars (height changes are this memo's own).
        if (Width == _lastWidth) return;
        _lastWidth = Width;
        if (!IsHandleCreated) { UpdateLayout(); return; }
        // Batch: a table relayout resizes every memo in it; updating each at once would re-lay
        // the table out once per memo. Instead all of them update together, after the layout,
        // with their containers' layout suspended — one relayout in total.
        Dirty.Add(this);
        PostBatch();
    }

    // Posted to the thread's synchronisation context, not to a memo: a memo whose form closes
    // before the post runs would drop it, leave _batchPosted set and stop every later batch.
    private static void PostBatch()
    {
        if (_batchPosted) return;
        _batchPosted = true;
        // never synchronously: this may run inside CreateHandle, where changing the scroll bars
        // (which recreates the window) would corrupt the handle being created
        var sync = System.Threading.SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();
        sync.Post(_ => FlushBatch(), null);
    }

    private static void FlushBatch()
    {
        _batchPosted = false;
        var memos = Dirty.Where(m => !m.IsDisposed && m.IsHandleCreated).ToList();
        Dirty.Clear();
        var parents = memos.Select(m => m.Parent).OfType<Control>().Distinct().ToList();
        foreach (Control p in parents) p.SuspendLayout();
        try { foreach (AiFgfMemo m in memos) m.UpdateLayout(); }
        finally { foreach (Control p in parents) p.ResumeLayout(performLayout: true); }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Measure();
        UpdateLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Measure();
        UpdateLayout();   // the scroll bar sizes change with DPI even when the font does not
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_recreating) return;   // a scroll-bar change: keep the caret where it was
        HookForm();                // the form is known by now (not when the row was built)
        Measure();
        ShowStart();
        // with the other memos of the window, in one relayout (see OnSizeChanged)
        _lastWidth = Width;
        Dirty.Add(this);
        PostBatch();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        HookForm();
    }

    private void HookForm()
    {
        if (FindForm() is not { } form) return;
        form.Shown -= OnFormShown;
        form.Shown += OnFormShown;
    }

    private void OnFormShown(object? sender, EventArgs e) => FitWindowWidth();

    /// <summary>Scroll to the first character (top-left), e.g. after the text was replaced.</summary>
    public void ShowStart()
    {
        if (!IsHandleCreated) return;
        SelectionStart = 0;
        SelectionLength = 0;
        ScrollToCaret();
    }
}
