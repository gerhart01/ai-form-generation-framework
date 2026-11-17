#if NET11_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// .NET 11: drag and drop onto a WinForms field - the view's half: it reads what is dragged (files and folders from
/// Explorer, text), asks <see cref="AiFgfDrop"/> (the shared decision) and shows it: a "copy" cursor and the field lit
/// while something it takes is over it, "no drop" otherwise; a drop sets the field's text (validation and conditions
/// follow as for typing). The rows of <see cref="FormScaffold"/> - text, file, folder, memo - take drops by themselves.
/// </summary>
public static class AiFgfDropView
{
    /// <summary>Let <paramref name="box"/> take drops as a <paramref name="target"/> field (a file field: matching <paramref name="filter"/>).</summary>
    public static T Accept<T>(T box, DropTarget target, string? filter = null) where T : TextBoxBase
    {
        ArgumentNullException.ThrowIfNull(box);
        box.AllowDrop = true;
        Color? restore = null;
        void Unlight()
        {
            if (restore is { } c) box.BackColor = c;
            restore = null;
        }
        bool Takes(DragEventArgs e) => box.Enabled && !box.ReadOnly && Read(e.Data) is var (paths, text) && AiFgfDrop.Accepts(target, paths, text, filter);

        box.DragEnter += (_, e) =>
        {
            bool ok = Takes(e);
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            if (ok && restore is null) { restore = box.BackColor; box.BackColor = AiFgfTheme.Current.HoverFill; }   // lit: it takes this
        };
        box.DragOver += (_, e) => e.Effect = Takes(e) ? DragDropEffects.Copy : DragDropEffects.None;
        box.DragLeave += (_, _) => Unlight();
        box.DragDrop += (_, e) =>
        {
            Unlight();
            if (!box.Enabled || box.ReadOnly) return;
            var (paths, text) = Read(e.Data);
            if (AiFgfDrop.Value(target, paths, text, box.Text, filter) is not { } value) return;
            box.Text = value;
            box.Focus();
            box.SelectionStart = box.TextLength;
        };
        return box;
    }

    /// <summary>The paths and the text of a drag (either may be null).</summary>
    internal static (IReadOnlyList<string>? Paths, string? Text) Read(IDataObject? data)
    {
        if (data is null) return (null, null);
        string[]? paths = data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null;
        string? text = data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string
                     : data.GetDataPresent(DataFormats.Text) ? data.GetData(DataFormats.Text) as string : null;
        return (paths, text);
    }
}
#endif
