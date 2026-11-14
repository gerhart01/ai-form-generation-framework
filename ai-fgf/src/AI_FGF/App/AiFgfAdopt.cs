using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// Moves an existing hand-built form (made in the Designer or in code, with absolute positions and its own
/// colours) onto AI_FGF one piece at a time. The form is not rebuilt and its logic is not touched.
/// <code>
/// AiFgfAdopt.Theme(this);              // theme colours, dark title bar, dark scroll bars and drop-downs
/// AutoMinSize.GuardPlaced(this);    // a minimum size worked out from the controls' positions and anchors
/// </code>
/// <para><see cref="Theme(Control)"/> gives every control the colours of its role in <see cref="AiFgfTheme.Current"/>:
/// window, field, button, the default button as the accent, grid, link. A colour the form set itself, such as a
/// LightGreen status label, keeps its hue. In a dark theme it gets a dark tone of that hue, and the text on it is
/// made to reach 4.5:1. <see cref="AiFgfTheme.Switch(AiFgfTheme)"/> themes the form again from the colours it first
/// had, so switching back to the light theme gives the original colours. Controls added later are themed too.
/// A control whose <see cref="Control.Tag"/> is <see cref="AiFgfTheme.KeepColors"/> is left alone, but its
/// children are not. AI_FGF's own controls are skipped because they theme themselves.</para>
/// </summary>
public static class AiFgfAdopt
{
    /// <summary>Minimum contrast of a control's text against its background after <see cref="Theme(Control)"/>.</summary>
    public const double MinContrast = 4.5;

    private sealed class Original
    {
        public Color Back, Fore, Link;          // Color.Empty: not set by the form (inherited or the default)
        public FlatStyle? Flat;
        public bool? VisualStyleBack;
    }

    private static readonly ConditionalWeakTable<Control, Original> Originals = new();
    private static readonly List<WeakReference<Control>> Roots = new();
    private static bool _listening;

    /// <summary>
    /// Theme <paramref name="root"/> and everything in it from <see cref="AiFgfTheme.Current"/>, now, on every theme
    /// switch, and for each control added later. Given a form, this also sets the title bar
    /// (<see cref="Dwm"/>, light or dark with the theme) and native dark scroll bars and drop-downs
    /// (<see cref="AiFgfNativeTheme"/>). Calling it again is harmless.
    /// </summary>
    public static void Theme(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!Roots.Any(r => r.TryGetTarget(out Control? c) && ReferenceEquals(c, root)))
        {
            Roots.RemoveAll(r => !r.TryGetTarget(out _));
            Roots.Add(new WeakReference<Control>(root));
        }
        if (!_listening) { AiFgfTheme.Changed += OnThemeChanged; _listening = true; }
        Apply(root, AiFgfTheme.Current);
        if (root is Form form)
        {
            Dwm.Apply(form, BackdropKind.None);
            AiFgfNativeTheme.Attach(form);
        }
    }

    private sealed record Placed(Control Control, Rectangle Bounds, Size ParentClient);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, List<Placed>> BeforeDpi = new();

    /// <summary>
    /// Keep a placed layout (coordinates and anchors) whole when the form moves to another DPI. A Designer form
    /// scales by its font (<see cref="AutoScaleMode.Font"/>): WinForms sets its controls back to their font-scaled
    /// sizes and then applies the window's shrink through the anchors once more, so going from 150 % to 100 % a
    /// stretching group box loses 270 px and a log anchored on four sides collapses to nothing. The layout is
    /// remembered before the change and laid out again after it, every distance scaled by the DPI ratio:
    /// margins kept to the edges a control is anchored to, a control anchored both ways filling the rest.
    /// </summary>
    public static void KeepLayoutAcrossDpi(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.DpiChanged -= OnLayoutDpiChanged;
        form.DpiChanged += OnLayoutDpiChanged;
    }

    private static void OnLayoutDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        // DpiChanged comes before the rescale: remember the layout now, lay it out again once the rescale is done
        if (sender is not Form f || !f.IsHandleCreated || f.IsDisposed || e.DeviceDpiOld <= 0) return;
        var before = new List<Placed>();
        Record(f, before);
        BeforeDpi.AddOrUpdate(f, before);
        double k = e.DeviceDpiNew / (double)e.DeviceDpiOld;
        f.BeginInvoke(() =>
        {
            if (f.IsDisposed || !BeforeDpi.TryGetValue(f, out List<Placed>? list)) return;
            BeforeDpi.Remove(f);
            f.SuspendLayout();
            try { foreach (Placed p in list) Restore(p, k); }   // parents before their children
            finally { f.ResumeLayout(true); }
        });
    }

    private static void Record(Control parent, List<Placed> into)
    {
        foreach (Control c in parent.Controls)
        {
            if (c.Dock != DockStyle.None) continue;
            into.Add(new Placed(c, c.Bounds, parent.ClientSize));
            if (c.Controls.Count > 0 && c is not (UpDownBase or ComboBox or TextBoxBase or DataGridView)) Record(c, into);
        }
    }

    private static void Restore(Placed p, double k)
    {
        Control c = p.Control;
        if (c.IsDisposed || c.Parent is not { } parent) return;
        Size client = parent.ClientSize;
        int S(int v) => (int)Math.Round(v * k);
        (int pos, int len) Axis(int lo, int len0, int parentLen0, int parentLen, bool near, bool far, int currentLen, bool autoSize)
        {
            int nearGap = S(lo), farGap = S(parentLen0 - lo - len0);
            int length = autoSize ? currentLen : S(len0);
            if (near && far) return (nearGap, Math.Max(0, parentLen - nearGap - farGap));
            if (far) return (parentLen - farGap - length, length);
            if (!near) return ((parentLen - length) / 2, length);   // centred: neither edge
            return (nearGap, length);
        }
        AnchorStyles a = c.Anchor;
        bool auto = c.AutoSize;
        var (x, w) = Axis(p.Bounds.X, p.Bounds.Width, p.ParentClient.Width, client.Width, (a & AnchorStyles.Left) != 0, (a & AnchorStyles.Right) != 0, c.Width, auto);
        var (y, h) = Axis(p.Bounds.Y, p.Bounds.Height, p.ParentClient.Height, client.Height, (a & AnchorStyles.Top) != 0, (a & AnchorStyles.Bottom) != 0, c.Height, auto);
        // a single-line box or a drop-down keeps the height its font gives it
        if (c is TextBoxBase { Multiline: false } or ComboBox or UpDownBase or DateTimePicker) h = c.Height;
        c.SetBounds(x, y, w, h);
    }

    /// <summary>
    /// Centre each row of controls placed side by side (a label, its text box, the Browse button beside it) on
    /// the row's tallest control, now and after every DPI change. A Designer form scales by its font, and at
    /// 144 DPI the rounding leaves a label 4–5 px off its text box. Only single-line controls (labels, inputs,
    /// buttons, check boxes) in the same container are moved, and only up or down.
    /// </summary>
    public static void AlignRows(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.DpiChanged -= OnAlignDpiChanged;
        form.DpiChanged += OnAlignDpiChanged;
        form.Shown -= OnAlignShown;
        form.Shown += OnAlignShown;
        Align(form);
    }

    private static void OnAlignShown(object? sender, EventArgs e) { if (sender is Form f) Align(f); }

    private static void OnAlignDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        // DpiChanged comes before the rescale: align once it is done
        if (sender is Form f && f.IsHandleCreated && !f.IsDisposed) f.BeginInvoke(() => { if (!f.IsDisposed) Align(f); });
    }

    private static bool RowItem(Control c) => c.Visible && c.Dock == DockStyle.None && c switch
    {
        Label { AutoSize: true } or CheckBox or RadioButton or ComboBox or UpDownBase or DateTimePicker => true,
        TextBoxBase t => !t.Multiline,
        Button b => b.Height <= b.Font.Height * 3,
        Label l => l.Height <= l.Font.Height * 2,
        _ => false,
    };

    private static void Align(Control container)
    {
        var items = container.Controls.Cast<Control>().Where(RowItem).OrderBy(c => c.Left).ToList();
        var done = new HashSet<Control>();
        foreach (Control seed in items.OrderByDescending(c => c.Height))
        {
            if (done.Contains(seed)) continue;
            // the row: controls overlapping the tallest one vertically, side by side with it and each other
            var row = items.Where(c => !done.Contains(c) && c.Top < seed.Bottom && c.Bottom > seed.Top).ToList();
            bool sideBySide = row.Count > 1 && row.Zip(row.Skip(1)).All(p => p.First.Right <= p.Second.Left + 1);
            done.UnionWith(row);
            if (!sideBySide) continue;
            int centre2 = seed.Top * 2 + seed.Height;
            foreach (Control c in row)
            {
                int top = (centre2 - c.Height) / 2;
                if (c != seed && Math.Abs(top - c.Top) > 0) c.Top = top;
            }
        }
        foreach (Control child in container.Controls)
            if (child.Controls.Count > 0 && child is not (UpDownBase or ComboBox or TextBoxBase or DataGridView)) Align(child);
    }

    /// <summary>True if <paramref name="root"/> was adopted with <see cref="Theme(Control)"/>.</summary>
    public static bool IsThemed(Control root) => Roots.Any(r => r.TryGetTarget(out Control? c) && ReferenceEquals(c, root));

    /// <summary>
    /// The background the form set itself, in <paramref name="theme"/>. In a light theme it is unchanged. In a
    /// dark theme a light colour becomes a dark tone of the same hue, so a green status stays green.
    /// </summary>
    public static Color OwnBack(Color own, AiFgfTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (!Dwm.IsDark(theme) || AiFgfColor.Luminance(own) < 0.12) return own;
        var (h, s, l) = AiFgfColor.ToHsl(own);
        // light tones (L 0.6–1) map onto 0.30–0.16, keeping the order: the lighter, the darker
        double dark = Math.Clamp(0.16 + (1 - l) * 0.35, 0.16, 0.32);
        return AiFgfColor.FromHsl(h, Math.Min(s, 0.55), dark);
    }

    private static void OnThemeChanged(object? sender, EventArgs e)
    {
        Roots.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (WeakReference<Control> r in Roots.ToList())
            if (r.TryGetTarget(out Control? c) && !c.IsDisposed) { Apply(c, AiFgfTheme.Current); c.Invalidate(true); }
    }

    private static void OnControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is { } c) Apply(c, AiFgfTheme.Current);
    }

    private static bool IsFrameworkControl(Control c) => c.GetType().Assembly == typeof(AiFgfAdopt).Assembly;

    private static Original Remember(Control c)
    {
        if (Originals.TryGetValue(c, out Original? o)) return o;
        var props = TypeDescriptor.GetProperties(c);
        o = new Original
        {
            Back = props["BackColor"]?.ShouldSerializeValue(c) == true ? c.BackColor : Color.Empty,
            Fore = props["ForeColor"]?.ShouldSerializeValue(c) == true ? c.ForeColor : Color.Empty,
            Link = c is LinkLabel l ? l.LinkColor : Color.Empty,
            Flat = c is Button b ? b.FlatStyle : null,
            VisualStyleBack = c switch { ButtonBase bb => bb.UseVisualStyleBackColor, TabPage p => p.UseVisualStyleBackColor, _ => null },
        };
        Originals.Add(c, o);
        return o;
    }

    /// <summary>A colour of its own: set by the form, and not a system colour (those are roles, as is Transparent).</summary>
    private static bool Own(Color c) => !c.IsEmpty && !c.IsSystemColor && c.A == 255;

    /// <summary>The theme role a system colour stands for.</summary>
    private static Color Role(Color system, AiFgfTheme t, bool fore, Color fallback)
    {
        if (!system.IsSystemColor) return fallback;
        return system.ToKnownColor() switch
        {
            KnownColor.Window or KnownColor.Info or KnownColor.ControlLightLight => fore ? fallback : t.Surface,
            KnownColor.Control or KnownColor.ButtonFace or KnownColor.Menu or KnownColor.MenuBar or KnownColor.AppWorkspace => fore ? fallback : t.App,
            KnownColor.Highlight or KnownColor.MenuHighlight or KnownColor.HotTrack => t.Accent,
            KnownColor.HighlightText => fore ? t.OnAccent : fallback,
            KnownColor.GrayText or KnownColor.InactiveCaptionText => fore ? t.Subtle : fallback,
            _ => fallback,
        };
    }

    private static Color EffectiveBack(Control c)
    {
        for (Control? p = c; p is not null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return AiFgfTheme.Current.App;
    }

    private static void Apply(Control c, AiFgfTheme t)
    {
        if (IsFrameworkControl(c)) return;   // AiFgfLog, ParamTable and the other AI_FGF controls theme themselves
        c.ControlAdded -= OnControlAdded;
        c.ControlAdded += OnControlAdded;
        c.BackColorChanged -= OnOwnColorChanged;
        c.BackColorChanged += OnOwnColorChanged;
        c.ForeColorChanged -= OnOwnColorChanged;
        c.ForeColorChanged += OnOwnColorChanged;
        bool outer = !_applying;
        _applying = true;
        try
        {
            if (!ReferenceEquals(c.Tag, AiFgfTheme.KeepColors)) ApplyOne(c, t);
            foreach (Control child in c.Controls) Apply(child, t);
        }
        finally { if (outer) _applying = false; }
    }

    private static bool _applying;

    /// <summary>
    /// The app set a colour itself at run time (a status label turned LightGreen): that is the control's own
    /// colour now, toned for the theme like the ones the form started with.
    /// </summary>
    private static void OnOwnColorChanged(object? sender, EventArgs e)
    {
        if (_applying || sender is not Control c || c.IsDisposed || ReferenceEquals(c.Tag, AiFgfTheme.KeepColors)) return;
        var props = TypeDescriptor.GetProperties(c);
        if (Originals.TryGetValue(c, out Original? o))
        {
            if (props["BackColor"]?.ShouldSerializeValue(c) == true) o.Back = c.BackColor;
            if (props["ForeColor"]?.ShouldSerializeValue(c) == true) o.Fore = c.ForeColor;
        }
        Apply(c, AiFgfTheme.Current);
    }

    private static void ApplyOne(Control c, AiFgfTheme t)
    {
        Original o = Remember(c);
        bool field = c is TextBoxBase or ComboBox or ListBox or UpDownBase or ListView or TreeView or DataGridView or DateTimePicker or MonthCalendar;

        // background: a colour of the form's own keeps its hue; a role (system colour or none) takes the theme's
        Color back;
        if (Own(o.Back)) back = OwnBack(o.Back, t);
        else if (field) back = Role(o.Back, t, false, t.Surface);
        else if (c is Button) back = Role(o.Back, t, false, t.Surface);
        else if (c is Form or TabPage || !o.Back.IsEmpty) back = Role(o.Back, t, false, t.App);
        else back = Color.Empty;   // inherits the parent's, which is themed

        if (c is Button { } button)
        {
            bool primary = button.FindForm() is { } f && ReferenceEquals(f.AcceptButton, button) && !Own(o.Back);
            if (primary) back = t.Accent;
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderColor = primary ? t.Accent : t.Border;
            button.FlatAppearance.MouseOverBackColor = primary ? t.AccentHover : Own(o.Back) ? AiFgfColor.Mix(back, t.Text, 0.08) : t.HoverFill;
            button.FlatAppearance.MouseDownBackColor = primary ? t.AccentHover : Own(o.Back) ? AiFgfColor.Mix(back, t.Text, 0.16) : t.PressFill;
            c.BackColor = back;
            c.ForeColor = EnsureFore(o.Fore, primary ? t.OnAccent : t.Text, back, t);
        }
        else
        {
            if (c is TabPage page) page.UseVisualStyleBackColor = false;
            if (back.IsEmpty) c.ResetBackColor(); else c.BackColor = back;
            Color backNow = EffectiveBack(c);
            if (field || c is Form or TabPage || !o.Fore.IsEmpty || AiFgfColor.Contrast(c.ForeColor, backNow) < MinContrast)
                c.ForeColor = EnsureFore(o.Fore, t.Text, backNow, t);
        }

        switch (c)
        {
            case LinkLabel link:
                link.LinkColor = AiFgfColor.EnsureContrast(Own(o.Link) && o.Link != Color.FromArgb(0, 0, 255) ? o.Link : t.Accent, MinContrast, EffectiveBack(c));
                link.ActiveLinkColor = link.LinkColor;
                link.VisitedLinkColor = link.LinkColor;
                break;
            case DataGridView g:
                g.EnableHeadersVisualStyles = false;
                g.BackgroundColor = t.Surface;
                g.GridColor = t.GridLines;
                g.DefaultCellStyle.BackColor = t.Surface;
                g.DefaultCellStyle.ForeColor = t.Text;
                g.DefaultCellStyle.SelectionBackColor = t.GridSelection;
                g.DefaultCellStyle.SelectionForeColor = t.Text;
                g.ColumnHeadersDefaultCellStyle.BackColor = t.GridHeader;
                g.ColumnHeadersDefaultCellStyle.ForeColor = t.Text;
                g.ColumnHeadersDefaultCellStyle.SelectionBackColor = t.GridHeader;
                g.ColumnHeadersDefaultCellStyle.SelectionForeColor = t.Text;
                g.RowHeadersDefaultCellStyle.BackColor = t.GridHeader;
                g.RowHeadersDefaultCellStyle.SelectionBackColor = t.GridHeader;
                g.RowHeadersDefaultCellStyle.SelectionForeColor = t.Text;
                g.RowHeadersDefaultCellStyle.ForeColor = t.Text;
                if (!g.AlternatingRowsDefaultCellStyle.BackColor.IsEmpty) g.AlternatingRowsDefaultCellStyle.BackColor = t.GridAlt;
                break;
            case ToolStrip strip:
                strip.RenderMode = ToolStripRenderMode.System;
                foreach (ToolStripItem item in strip.Items) { item.BackColor = back.IsEmpty ? t.App : back; item.ForeColor = t.Text; }
                break;
        }
    }

    /// <summary>The text colour: the form's own one (kept, then made readable) or the theme's.</summary>
    private static Color EnsureFore(Color own, Color role, Color back, AiFgfTheme t)
    {
        Color fore = Own(own) ? own : Role(own, t, true, role);
        // the form's own dark text on a background turned dark (or light on light): take the theme's text instead
        if (Own(own) && AiFgfColor.Contrast(fore, back) < 2.0) fore = AiFgfColor.Contrast(t.Text, back) >= AiFgfColor.Contrast(t.OnAccent, back) ? t.Text : t.OnAccent;
        return AiFgfColor.EnsureContrast(fore, MinContrast, back);
    }
}
