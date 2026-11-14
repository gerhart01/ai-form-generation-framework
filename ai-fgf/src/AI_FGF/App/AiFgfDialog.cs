using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// A small modal prompt — "Add target", "Credentials", "Rename" — built from the same rows as a
/// tool form: OK validates (the dialog stays open while a field is invalid), Enter is OK, Esc is
/// Cancel, and the dialog sizes itself to its content at any DPI.
/// <code>
/// var v = AiFgfDialog.Ask(this, "Add target", rows => rows
///     .Text("Host:", required: true)
///     .Number("Port:", min: 1, max: 65535, value: 443), okText: "&amp;Add");
/// if (v is null) return;          // cancelled
/// string host = v["Host"];
/// </code>
/// </summary>
public sealed class AiFgfDialog : Form
{
    private readonly FormScaffold _scaffold;
    private readonly GroupBox? _params;
    private readonly FlowLayoutPanel _bar;

    private AiFgfDialog(string title, Action<FormScaffold.RowBuilder> build, string okText, string cancelText)
    {
        Text = title;
        _scaffold = FormScaffold.For(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        Button ok = null!, cancel = null!;
        _scaffold
            .Parameters("", build)
            .Buttons(bar => bar
                .Button(cancelText, (_, _) => { }, out cancel)
                .Primary(okText, (_, _) => Accept(), out ok), BarAlign.Right);
        _scaffold.Done();
        Ok = ok;
        Cancel = cancel;
        Cancel.DialogResult = DialogResult.Cancel;   // a click (or Esc) closes a modal dialog
        AcceptButton = Ok;                            // Enter → OK → validation
        CancelButton = Cancel;
        _bar = _scaffold.ButtonBar!;
        foreach (Control c in Controls) if (c is GroupBox g) _params = g;
    }

    /// <summary>Build a dialog without showing it (for custom handling, and for tests).</summary>
    public static AiFgfDialog Create(string title, Action<FormScaffold.RowBuilder> build, string? okText = null,
                                  IReadOnlyDictionary<string, string>? initial = null, string? cancelText = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        var dialog = new AiFgfDialog(title, build, okText ?? AiFgfText.Ok, cancelText ?? AiFgfText.Cancel);
        if (initial is not null) dialog.Fields.SetValues(initial);
        return dialog;
    }

    /// <summary>
    /// Show a modal dialog and return the values of its active fields (numbers invariant,
    /// checkboxes "true"/"false"), or null if it was cancelled.
    /// </summary>
    public static Dictionary<string, string>? Ask(IWin32Window? owner, string title, Action<FormScaffold.RowBuilder> build,
                                                  string? okText = null, IReadOnlyDictionary<string, string>? initial = null)
    {
        using AiFgfDialog dialog = Create(title, build, okText, initial);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Values : null;
    }

    /// <summary>The dialog's fields, by key.</summary>
    public FieldSet Fields => _scaffold.Fields;

    public Button Ok { get; }

    public Button Cancel { get; }

    /// <summary>The active field values once OK was accepted; null otherwise.</summary>
    public Dictionary<string, string>? Values => DialogResult == DialogResult.OK ? Fields.GetActiveValues() : null;

    /// <summary>What OK does: validate, and accept only when every active field is valid.</summary>
    public bool Accept()
    {
        if (Fields.Validate().Count > 0) return false;   // invalid fields are marked; the first is focused
        DialogResult = DialogResult.OK;                   // closes a modal dialog
        return true;
    }

    /// <summary>
    /// Size the dialog to its content: wide enough that the rows stay side by side (label left
    /// of input) and the buttons sit on one line, and exactly as tall as the rows plus the bar.
    /// A dialog is not resizable, so that size is also its minimum.
    /// </summary>
    public void FitToContent()
    {
        MinimumSize = System.Drawing.Size.Empty;
        int width = LogicalToDeviceUnits(420);
        if (_scaffold.ParamTable is { } table && _params is not null)
        {
            int groupChrome = _params.Width - _params.DisplayRectangle.Width;
            // one em of headroom past the breakpoint: the table's hysteresis un-stacks only there
            width = Math.Max(width, table.StackBreakpoint + Font.Height + groupChrome + Padding.Horizontal);
        }
        int buttons = _bar.Padding.Horizontal + _bar.Controls.Cast<Control>().Sum(b => b.GetPreferredSize(System.Drawing.Size.Empty).Width + b.Margin.Horizontal);
        width = Math.Max(width, buttons + Padding.Horizontal);
        ClientSize = new System.Drawing.Size(width, ClientSize.Height);
        PerformLayout();
        int content = Padding.Vertical + (_params?.Height ?? 0) + _bar.Height;
        ClientSize = new System.Drawing.Size(width, content);
        MinimumSize = Size;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToContent();
        if (Fields.Keys.Count > 0) ActiveControl = Fields[Fields.Keys[0]];
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // after WinForms has rescaled fonts and bounds
        BeginInvoke(() => { if (!IsDisposed) FitToContent(); });
    }
}
