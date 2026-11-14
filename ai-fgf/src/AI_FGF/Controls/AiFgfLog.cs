using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// A dark, console-style log pane with thread-safe coloured append and a built-in
/// find bar (Ctrl+F, Enter / Shift+Enter to navigate, Esc to leave).
///
/// This packages the <c>AppendLine</c> + <c>FindInLog</c> code that every reference
/// GUI re-implemented. Append is safe to call from a background thread — it marshals
/// to the UI thread itself and silently drops writes once the control is disposed,
/// which is exactly what a streaming process needs at shutdown.
/// </summary>
public sealed class AiFgfLog : UserControl
{
    private readonly RichTextBox _text;
    private readonly TextBox _find;
    private readonly Label _findInfo;
    private readonly FlowLayoutPanel _findBar;
    private volatile bool _closing;

    private readonly ConcurrentQueue<(string Text, Color? Color)> _pending = new();
    private readonly int _uiThread = Environment.CurrentManagedThreadId; // the thread that created the control
    private int _flushScheduled;

    public AiFgfLog()
    {
        var t = AiFgfTheme.Current;

        _text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = t.LogBackground,
            ForeColor = t.LogForeground,
            Font = t.MonoFont(),
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            HideSelection = false, // keep the found match visible when focus is in the find box
        };

        // AutoSize + wrap so a narrow window wraps the find controls instead of clipping them.
        var findBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(6, 4, 6, 0),
            BackColor = t.App,
        };
        AiFgf.CentreRows(findBar);   // label, box and buttons on one centre line at every DPI
        findBar.Controls.Add(new Label { Text = AiFgfText.Find, AutoSize = true, Margin = new Padding(0, 8, 6, 0), ForeColor = t.Text });
        _find = new TextBox
        {
            Width = 260,
            Margin = new Padding(0, 4, 6, 0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = t.Surface,   // themed: an unthemed box stays white in the dark theme
            ForeColor = t.Text,
        };
        _find.TextChanged += (_, _) => FindNext(forward: true, fromStart: true);
        _find.KeyDown += OnFindKeyDown;
        _findInfo = new Label { Text = "", AutoSize = true, Margin = new Padding(8, 8, 0, 0), ForeColor = Color.Gray };
        findBar.Controls.Add(_find);
        AiFgf.FitWidthToBar(_find, findBar, 260);
        findBar.Controls.Add(AiFgf.SmallButton(AiFgfText.FindNext, (_, _) => FindNext(forward: true)));
        findBar.Controls.Add(AiFgf.SmallButton(AiFgfText.FindPrevious, (_, _) => FindNext(forward: false)));
        findBar.Controls.Add(AiFgf.SmallButton(AiFgfText.ClearLog, (_, _) => Clear()));
        findBar.Controls.Add(_findInfo);

        Controls.Add(_text);    // Fill first (lowest z)
        Controls.Add(findBar);  // then Top
        _findBar = findBar;

        // Ctrl+F from anywhere in the pane focuses the find box.
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.F) { _find.Focus(); _find.SelectAll(); e.Handled = true; } };
    }

    /// <summary>Put the caret in the find box (Ctrl+F).</summary>
    /// <summary>
    /// The least height the log needs at <paramref name="width"/>: its find bar as it wraps at that width, and
    /// three full lines of text (the rule AiFgfConformance holds a log to). AutoMinSize keeps the window that tall.
    /// </summary>
    public int MinimumHeightAt(int width)
    {
        int bar = _findBar.Visible ? _findBar.GetPreferredSize(new Size(Math.Max(1, width), 0)).Height : 0;   // the layout engine itself: rows centred, tall buttons
        return bar + (_text.Height - _text.ClientSize.Height) + 3 * _text.Font.Height;
    }

    public void FocusFind()
    {
        _find.Focus();
        _find.SelectAll();
    }

    /// <summary>
    /// Call when the owner is closing: lines already logged are still written (if called on the
    /// UI thread), later cross-thread writes are dropped.
    /// </summary>
    public void MarkClosing()
    {
        if (OnUiThread) Flush();
        _closing = true;
    }

    private bool OnUiThread => Environment.CurrentManagedThreadId == _uiThread;

    public void Clear()
    {
        if (_closing || IsDisposed) return;
        if (!OnUiThread) { try { _text.BeginInvoke(Clear); } catch { } return; }
        while (_pending.TryDequeue(out _)) { }
        _text.Clear();
    }

    /// <summary>
    /// Oldest lines are dropped once the log holds more than this many, so a chatty process
    /// can't grow the control (and its memory) without bound.
    /// </summary>
    [DefaultValue(50_000)]
    public int MaxLines { get; set; } = 50_000;

    /// <summary>Lines logged so far (on the UI thread this first writes anything still queued).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LineCount { get { FlushIfUiThread(); return WrittenLineCount; } }

    private int WrittenLineCount => _text.GetLineFromCharIndex(_text.TextLength);

    /// <summary>The lines logged so far, without the trailing empty line (UI thread: flushes first).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string[] Lines
    {
        get
        {
            FlushIfUiThread();
            string[] lines = _text.Lines;
            return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
        }
    }

    private void FlushIfUiThread() { if (OnUiThread && _text.IsHandleCreated) Flush(); }

    /// <summary>Lines queued from background threads but not yet written.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int PendingCount => _pending.Count;

    /// <summary>How many batched writes have happened (diagnostics: far fewer than lines appended).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int FlushCount { get; private set; }

    // ------------------------------------------------------------- appending

    /// <summary>Append a line in the theme's default log colour.</summary>
    public void Line(string text) => Append(text, null);

    /// <summary>Append an error line (salmon).</summary>
    public void Error(string text) => Append(text, AiFgfTheme.Current.LogError);

    /// <summary>Append a success line (green).</summary>
    public void Success(string text) => Append(text, AiFgfTheme.Current.LogSuccess);

    /// <summary>Append a warning line (orange).</summary>
    public void Warn(string text) => Append(text, AiFgfTheme.Current.LogWarn);

    /// <summary>
    /// Append a line in an explicit colour. Safe from any thread, and cheap from all of them:
    /// lines are queued and written in batches — one posted UI flush per burst instead of an
    /// append, a trim and a scroll per line — so neither a process printing thousands of lines
    /// a second nor a UI-thread loop logging thousands of lines can freeze the window (writing
    /// each line immediately is O(n²) as the text grows). Order is preserved. Reading
    /// <see cref="Lines"/>/<see cref="LineCount"/> on the UI thread writes anything still queued.
    /// </summary>
    public void Append(string text, Color? color)
    {
        if (_closing || IsDisposed) return;
        _pending.Enqueue((text, color));

        // Marshal through this control, not the RichTextBox: our handle is created before the
        // child's (and survives a child handle re-creation), so there is no window in which a
        // line can be queued with no flush scheduled. Before our handle exists, OnHandleCreated
        // schedules the catch-up flush.
        if (!IsHandleCreated) return;
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
        {
            try { BeginInvoke(Flush); }
            catch { Interlocked.Exchange(ref _flushScheduled, 0); /* closing */ }
        }
    }

    /// <summary>Write every queued line now. Must be called on the UI thread.</summary>
    public void Flush()
    {
        // Reset first: a line enqueued after this point schedules a fresh flush, so none is lost.
        Interlocked.Exchange(ref _flushScheduled, 0);
        if (_closing || IsDisposed || _pending.IsEmpty) return;
        FlushCount++;

        // Follow the tail only if the user is at the bottom with nothing selected; otherwise keep
        // their find match / selection and scroll position while output keeps streaming.
        bool follow = IsFollowingTail();
        int selStart = _text.SelectionStart, selLength = _text.SelectionLength;
        int firstVisible = FirstVisibleLine();
        int trimmedChars = 0, trimmedLines = 0;

        SetRedraw(false);
        try
        {
            // Coalesce consecutive same-coloured lines into one AppendText call.
            var run = new StringBuilder();
            Color? runColor = null;
            bool any = false;
            while (_pending.TryDequeue(out var item))
            {
                if (any && item.Color != runColor) { Write(run, runColor); run.Clear(); }
                runColor = item.Color;
                any = true;
                run.Append(item.Text).Append(Environment.NewLine);
            }
            if (run.Length > 0) Write(run, runColor);
            (trimmedChars, trimmedLines) = TrimToMaxLines();

            if (!follow)
            {
                int start = selStart - trimmedChars;
                if (start < 0) { selLength = Math.Max(0, selLength + start); start = 0; } // selection partly trimmed away
                _text.Select(start, selLength);
                ScrollToLine(Math.Max(0, firstVisible - trimmedLines));
            }
        }
        finally
        {
            SetRedraw(true);
        }
        if (follow)
        {
            _text.SelectionStart = _text.TextLength;
            _text.SelectionLength = 0;
            _text.ScrollToCaret();
        }
    }

    private bool IsFollowingTail()
    {
        if (!_text.IsHandleCreated || _text.TextLength == 0) return true;
        if (_text.SelectionLength != 0 || _text.SelectionStart != _text.TextLength) return false;
        // caret at the end — and is the end on screen? (the user may have scrolled up with the wheel)
        Point last = _text.GetPositionFromCharIndex(Math.Max(0, _text.TextLength - 1));
        return last.Y < _text.ClientSize.Height;
    }

    private int FirstVisibleLine()
        => _text.IsHandleCreated ? (int)SendMessage(_text.Handle, EM_GETFIRSTVISIBLELINE, 0, 0) : 0;

    private void ScrollToLine(int line)
    {
        if (!_text.IsHandleCreated) return;
        int delta = line - FirstVisibleLine();
        if (delta != 0) SendMessage(_text.Handle, EM_LINESCROLL, 0, delta);
    }

    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_LINESCROLL = 0x00B6;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_pending.IsEmpty) BeginInvoke(Flush);
    }

    private void Write(StringBuilder run, Color? color)
    {
        _text.SelectionStart = _text.TextLength;
        _text.SelectionLength = 0;
        _text.SelectionColor = color ?? _text.ForeColor;
        _text.AppendText(run.ToString());
        _text.SelectionColor = _text.ForeColor;
    }

    /// <summary>Drop the oldest lines past MaxLines; returns how many characters and lines were removed.</summary>
    private (int chars, int lines) TrimToMaxLines()
    {
        int excess = WrittenLineCount - Math.Max(1, MaxLines);   // not LineCount: that would re-enter Flush
        if (excess <= 0) return (0, 0);
        int cut = _text.GetFirstCharIndexFromLine(excess);
        if (cut <= 0) return (0, 0);
        bool readOnly = _text.ReadOnly;
        _text.ReadOnly = false;           // RichEdit refuses programmatic edits while read-only
        _text.Select(0, cut);
        _text.SelectedText = string.Empty;
        _text.ReadOnly = readOnly;
        return (cut, excess);
    }

    private void SetRedraw(bool on)
    {
        if (!_text.IsHandleCreated) return;
        SendMessage(_text.Handle, WM_SETREDRAW, on ? 1 : 0, 0);
        if (on) _text.Invalidate();
    }

    private const int WM_SETREDRAW = 0x000B;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    // ---------------------------------------------------------------- finding

    private void OnFindKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { FindNext(forward: !e.Shift); e.Handled = e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Escape) { _text.Focus(); e.Handled = e.SuppressKeyPress = true; }
    }

    private void FindNext(bool forward, bool fromStart = false)
    {
        string term = _find.Text;
        if (term.Length == 0) { _findInfo.Text = ""; _text.Select(_text.SelectionStart, 0); return; }

        int total = CountOccurrences(_text.Text, term);
        if (total == 0) { _findInfo.Text = AiFgfText.NoMatches; _findInfo.ForeColor = Color.Firebrick; return; }
        _findInfo.ForeColor = Color.Gray;

        int selStart = _text.SelectionStart, selLen = _text.SelectionLength, idx;
        if (forward)
        {
            int from = fromStart ? selStart : selStart + selLen;
            idx = _text.Find(term, from, RichTextBoxFinds.None);
            if (idx < 0) idx = _text.Find(term, 0, RichTextBoxFinds.None); // wrap
        }
        else
        {
            idx = selStart > 0 ? _text.Find(term, 0, selStart, RichTextBoxFinds.Reverse) : -1;
            if (idx < 0) idx = _text.Find(term, 0, _text.TextLength, RichTextBoxFinds.Reverse); // wrap
        }
        if (idx < 0) { _findInfo.Text = $"0 / {total}"; return; }

        _text.Select(idx, term.Length);
        _text.ScrollToCaret();
        int ordinal = CountOccurrences(_text.Text[..idx], term) + 1;
        _findInfo.Text = $"{ordinal} / {total}";
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0) return 0;
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.OrdinalIgnoreCase)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
