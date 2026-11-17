using System;
using System.Drawing;
using System.Windows.Forms;
using K = AI_FGF.AiFgfLayoutSnapshot.Kinds;

namespace AI_FGF;

/// <summary>
/// Takes an <see cref="AiFgfLayoutSnapshot"/> of a shown WinForms form — any form, not only AI_FGF
/// ones: snapshot an existing hand-made form and its AI_FGF replacement and
/// <see cref="AiFgfLayoutSnapshot.Compare"/> them, or compare against the Avalonia version of the same form.
/// </summary>
public static class AiFgfSnapshot
{
    public static AiFgfLayoutSnapshot Of(Form form) => Of(form, everyTabPage: false);

    /// <summary>
    /// The snapshot; with <paramref name="everyTabPage"/> the controls of every tab page, not only the selected one's (each
    /// page is selected in turn, then the selection restored) - what a migration must carry over.
    /// </summary>
    public static AiFgfLayoutSnapshot Of(Form form, bool everyTabPage)
    {
        ArgumentNullException.ThrowIfNull(form);
        double scale = form.DeviceDpi / 96.0;
        var snap = new AiFgfLayoutSnapshot
        {
            Toolkit = "WinForms",
            Width = AiFgfLayoutSnapshot.R(form.ClientSize.Width / scale),
            Height = AiFgfLayoutSnapshot.R(form.ClientSize.Height / scale),
        };

        void Add(string kind, string text, Rectangle screen, Font? font)
        {
            Rectangle r = form.RectangleToClient(screen);
            snap.Elements.Add(new AiFgfElement(kind, text,
                AiFgfLayoutSnapshot.R(r.X / scale), AiFgfLayoutSnapshot.R(r.Y / scale), AiFgfLayoutSnapshot.R(r.Width / scale), AiFgfLayoutSnapshot.R(r.Height / scale),
                font?.Name ?? "", font is null ? 0 : Math.Round(font.SizeInPoints, 2), font?.Bold ?? false));
        }
        Rectangle Screen(Control c) => c.Parent is { } p ? p.RectangleToScreen(c.Bounds) : c.Bounds;
        string Plain(string? s) => AiFgfLayoutSnapshot.Plain(s, '&');

        void Walk(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                switch (c)
                {
                    case GroupBox g:
                        if (g.Text.Length > 0) Add(K.Group, Plain(g.Text), Screen(g), g.Font);
                        Walk(g);
                        break;
                    case TabControl tabs:
                        for (int i = 0; i < tabs.TabPages.Count; i++)
                            Add(K.Tab, Plain(tabs.TabPages[i].Text), tabs.RectangleToScreen(tabs.GetTabRect(i)), tabs.Font);
                        if (everyTabPage)
                        {
                            int selected = tabs.SelectedIndex;
                            for (int i = 0; i < tabs.TabCount; i++) { tabs.SelectedIndex = i; Walk(tabs.TabPages[i]); }
                            if (selected >= 0) tabs.SelectedIndex = selected;
                        }
                        else if (tabs.SelectedTab is { } page) Walk(page);
                        break;
                    case StatusStrip strip:
                        foreach (ToolStripItem item in strip.Items)
                            if (item.Visible) Add(K.Status, Plain(item.Text), strip.RectangleToScreen(item.Bounds), item.Font);
                        break;
                    case CheckBox or RadioButton: Add(K.Check, Plain(c.Text), Screen(c), c.Font); break;
                    case ButtonBase: Add(K.Button, Plain(c.Text), Screen(c), c.Font); break;
                    case Label l when l.Text.Length > 0: Add(K.Label, l.UseMnemonic ? Plain(l.Text) : l.Text, Screen(c), c.Font); break;
                    case RichTextBox: Add(K.Log, "", Screen(c), c.Font); break;
                    case TextBox { UseSystemPasswordChar: true } or TextBox { PasswordChar: not '\0' }:
                        Add(K.Text, "", Screen(c), c.Font);   // a password: never its value (the snapshot is saved to a file)
                        break;
                    case TextBoxBase: Add(K.Text, c.Text, Screen(c), c.Font); break;
                    case NumericUpDown: Add(K.Number, c.Text, Screen(c), c.Font); break;
                    case ComboBox: Add(K.Combo, c.Text, Screen(c), c.Font); break;
                    case DataGridView: Add(K.Grid, "", Screen(c), c.Font); break;
                    case ProgressBar: Add(K.Progress, "", Screen(c), null); break;
                    default: Walk(c); break;
                }
            }
        }
        Walk(form);
        return snap;
    }
}
