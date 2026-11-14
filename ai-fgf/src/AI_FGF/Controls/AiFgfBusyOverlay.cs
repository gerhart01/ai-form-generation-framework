using System;
using System.Drawing;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// A translucent "busy" scrim shown over a form during a long operation. It is a borderless,
/// click-blocking layered window (so it genuinely dims the content beneath, which a child
/// control cannot do in WinForms) that tracks the owner as it moves and resizes.
///
/// <code>
/// var busy = new AiFgfBusyOverlay();
/// busy.ShowOver(this, "Working…");
/// // …later…
/// busy.HideOverlay();
/// </code>
/// </summary>
public sealed class AiFgfBusyOverlay : Form
{
    private readonly Label _label;
    private Form? _owner;

    public AiFgfBusyOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ControlBox = false;
        StartPosition = FormStartPosition.Manual;
        Opacity = 0.55;                      // let the dimmed content show through
        BackColor = Color.FromArgb(18, 20, 24);
        DoubleBuffered = true;

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font(AiFgfTheme.Current.UiFontFamily, 12f, FontStyle.Bold),
            UseMnemonic = false,
        };
        Controls.Add(_label);
    }

    /// <summary>Don't steal focus/activation when shown.</summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>
    /// Refuse a user close (e.g. Alt+F4 after clicking the scrim): closing a modeless form
    /// disposes it, and the owner would then reuse a dead overlay on the next run. Closes
    /// driven by the owner or the application still go through.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }

    /// <summary>Show the scrim over <paramref name="owner"/>'s client area with a message.</summary>
    public void ShowOver(Form owner, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner is not null) Detach();

        _owner = owner;
        _label.Text = message ?? AiFgfText.Working;
        Owner = owner;
        owner.LocationChanged += Track;
        owner.SizeChanged += Track;
        owner.Move += Track;

        Reposition();
        if (!Visible) Show(owner);
        Reposition();
        BringToFront();
    }

    /// <summary>Update the message while shown.</summary>
    public void SetMessage(string message)
    {
        if (_label.InvokeRequired) { try { _label.BeginInvoke(() => _label.Text = message); } catch { } return; }
        _label.Text = message ?? AiFgfText.Working;
    }

    /// <summary>Hide the scrim and stop tracking the owner.</summary>
    public void HideOverlay()
    {
        Detach();
        if (Visible) Hide();
    }

    private void Detach()
    {
        if (_owner is null) return;
        _owner.LocationChanged -= Track;
        _owner.SizeChanged -= Track;
        _owner.Move -= Track;
        _owner = null;
    }

    private void Track(object? sender, EventArgs e) => Reposition();

    private void Reposition()
    {
        if (_owner is null || _owner.IsDisposed) return;
        // Cover exactly the owner's client area (screen coordinates).
        Bounds = _owner.RectangleToScreen(_owner.ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Detach();
        base.Dispose(disposing);
    }
}
