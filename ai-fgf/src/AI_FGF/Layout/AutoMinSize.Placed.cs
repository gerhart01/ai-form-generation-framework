using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

public static partial class AutoMinSize
{
    /// <summary>The least gap, in logical pixels, two controls placed side by side may shrink to.</summary>
    public const int PlacedMinGap = 6;

    /// <summary>The margin, in logical pixels, a control keeps from the window's edge as the window shrinks (or less if it had less).</summary>
    public const int PlacedEdge = 12;

    /// <summary>
    /// <see cref="Guard"/> for a form whose controls are placed by coordinates and anchors (a Designer form,
    /// a hand-coded one) rather than by AI_FGF. The minimum size is how far the form can shrink while every
    /// control keeps the rules below. It is worked out from the controls' current positions and anchors, so it
    /// follows the DPI and can never be larger than the form's current size:
    /// <list type="bullet">
    /// <item>a control anchored only Left/Top does not move, so the form stays wide/tall enough for it and its margin;</item>
    /// <item>a control anchored only Right/Bottom moves, and stops at the left/top edge;</item>
    /// <item>a control anchored both ways stretches, and keeps its caption whole (a button, a check box, a label),
    /// a usable width (a text box, a list), or what its own placed children need (a group box, a panel);</item>
    /// <item>two controls side by side keep at least <see cref="PlacedMinGap"/> px between them, or their gap if it was smaller.</item>
    /// </list>
    /// Docked panels and a wrapping <paramref name="bar"/> count as for <see cref="Guard"/>. It is applied again
    /// on a DPI change, when the form is shown, and when a control is shown, hidden, added or removed. After
    /// moving controls in code, call <see cref="Refit"/>.
    /// </summary>
    public static void GuardPlaced(Form form, FlowLayoutPanel? bar = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        Configs.AddOrUpdate(form, new Config(bar, 0, 0, Placed: true));
        WatchPlaced(form, form);
        Guard(form, bar, 0, 0);
        // Guard replaced the config; keep this one
        Configs.AddOrUpdate(form, new Config(bar, 0, 0, Placed: true));
        Apply(form);
    }

    /// <summary>
    /// The least client size the controls placed on <paramref name="form"/> allow (see <see cref="GuardPlaced"/>),
    /// in device pixels at its current DPI.
    /// </summary>
    public static Size PlacedMinClient(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return MinContent(form, form.ClientSize);
    }

    private static void WatchPlaced(Form form, Control root)
    {
        foreach (Control c in root.Controls)
        {
            c.VisibleChanged -= OnPlacedChanged;
            c.VisibleChanged += OnPlacedChanged;
            WatchPlaced(form, c);
        }
        root.ControlAdded -= OnPlacedAdded;
        root.ControlAdded += OnPlacedAdded;
        root.ControlRemoved -= OnPlacedChanged;
        root.ControlRemoved += OnPlacedChanged;
    }

    private static readonly HashSet<Form> Pending = new();

    private static void OnPlacedAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is { } c && c.FindForm() is { } f) WatchPlaced(f, c.Parent ?? c);
        OnPlacedChanged(sender, e);
    }

    /// <summary>A control shown, hidden, added or removed: re-apply once, after the current batch of changes.</summary>
    private static void OnPlacedChanged(object? sender, EventArgs e)
    {
        if (sender is not Control c || c.FindForm() is not { } f || f.IsDisposed) return;
        if (!f.IsHandleCreated || !f.Visible) return;   // Shown applies it
        if (!Pending.Add(f)) return;
        f.BeginInvoke(() => { Pending.Remove(f); if (!f.IsDisposed) Apply(f); });
    }

    /// <summary>How an edge follows the container's size: 0 = stays, 1 = moves with it, 0.5 = centred.</summary>
    private static (double Near, double Far) Follow(AnchorStyles a, AnchorStyles near, AnchorStyles far)
    {
        bool n = (a & near) != 0, f = (a & far) != 0;
        if (n && f) return (0, 1);
        if (f) return (1, 1);
        if (n) return (0, 0);
        return (0.5, 0.5);
    }

    /// <summary>The least client size of <paramref name="container"/> its placed (undocked) children allow.</summary>
    private static Size MinContent(Control container, Size client)
    {
        var placed = container.Controls.Cast<Control>().Where(c => c.Visible && c.Dock == DockStyle.None).ToList();
        if (placed.Count == 0) return Size.Empty;
        int gap = container.LogicalToDeviceUnits(PlacedMinGap), edge = container.LogicalToDeviceUnits(PlacedEdge);
        var floors = placed.ToDictionary(c => c, Floor);

        int Axis(bool horizontal)
        {
            int size0 = horizontal ? client.Width : client.Height;
            AnchorStyles near = horizontal ? AnchorStyles.Left : AnchorStyles.Top, far = horizontal ? AnchorStyles.Right : AnchorStyles.Bottom;
            double need = 0;
            foreach (Control c in placed)
            {
                var (kn, kf) = Follow(c.Anchor, near, far);
                int lo = horizontal ? c.Left : c.Top, hi = horizontal ? c.Right : c.Bottom;
                int len = hi - lo, floor = horizontal ? floors[c].Width : floors[c].Height;
                // stays in: an edge that does not move keeps its margin (up to the usual window margin) …
                if (kf == 0) need = Math.Max(need, hi + Math.Min(size0 - hi, edge));
                else if (kf < 1) need = Math.Max(need, size0 - (size0 - hi - Math.Min(size0 - hi, edge)) / (1 - kf));   // centred
                // … the near edge stops at 0
                if (kn > 0) need = Math.Max(need, size0 - (lo - Math.Min(lo, edge)) / kn);
                // a stretching control keeps its floor
                if (kf - kn > 0 && len > floor) need = Math.Max(need, size0 - (len - floor) / (kf - kn));
                else if (kf - kn > 0) need = Math.Max(need, size0);
            }
            // two controls side by side keep their gap (or the least gap)
            foreach (Control a in placed)
                foreach (Control b in placed)
                {
                    if (ReferenceEquals(a, b)) continue;
                    int aHi = horizontal ? a.Right : a.Bottom, bLo = horizontal ? b.Left : b.Top;
                    if (aHi > bLo) continue;
                    // only neighbours across this axis: their extents on the other axis overlap
                    int aO1 = horizontal ? a.Top : a.Left, aO2 = horizontal ? a.Bottom : a.Right;
                    int bO1 = horizontal ? b.Top : b.Left, bO2 = horizontal ? b.Bottom : b.Right;
                    if (aO2 <= bO1 || bO2 <= aO1) continue;
                    double closing = Follow(b.Anchor, near, far).Near - Follow(a.Anchor, near, far).Far;
                    if (closing <= 0) continue;
                    int gap0 = bLo - aHi, least = Math.Min(gap0, gap);
                    need = Math.Max(need, size0 - (gap0 - least) / closing);
                }
            return (int)Math.Ceiling(Math.Min(need, size0));
        }

        return new Size(Axis(true), Axis(false));
    }

    /// <summary>The least size a stretching control may take.</summary>
    private static Size Floor(Control c)
    {
        Size min = c.MinimumSize;
        Size Cap(Size s) => new(Math.Max(min.Width, Math.Min(s.Width, c.Width)), Math.Max(min.Height, Math.Min(s.Height, c.Height)));
        // a container: what its own placed children need, plus its frame (a group box's caption and border)
        if (c.Controls.Count > 0 && c is not (UpDownBase or ComboBox or DataGridView or TextBoxBase))
        {
            Size frame = new(c.Width - c.DisplayRectangle.Width, c.Height - c.DisplayRectangle.Height);
            Size content = MinContent(c, c.DisplayRectangle.Size);
            if (content.IsEmpty) content = new Size(c.LogicalToDeviceUnits(60), c.LogicalToDeviceUnits(40));
            if (c is GroupBox) content.Width = Math.Max(content.Width, TextRenderer.MeasureText(c.Text, c.Font).Width + c.LogicalToDeviceUnits(16) - frame.Width);
            return Cap(new Size(content.Width + frame.Width, content.Height + frame.Height));
        }
        return c switch
        {
            // the caption stays whole
            ButtonBase or Label => Cap(c.GetPreferredSize(Size.Empty)),
            // single-line inputs: a few characters; lists, grids, memos: a few lines too
            TextBoxBase { Multiline: false } or ComboBox or UpDownBase or DateTimePicker => Cap(new Size(c.LogicalToDeviceUnits(60), c.Height)),
            _ => Cap(new Size(c.LogicalToDeviceUnits(60), c.LogicalToDeviceUnits(40))),
        };
    }
}
