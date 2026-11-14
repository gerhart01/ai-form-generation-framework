using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// Moving an existing hand-made form to AI_FGF. <see cref="DraftSpec"/> reads a shown form — its
/// labels, text boxes, Browse buttons, spinners, drop-downs and check boxes — into a <see cref="ToolSpec"/>
/// draft: one field per input, in reading order, with the label, kind, current value as default, range,
/// choices. What a form cannot tell (the executable, each field's <c>arg</c>) is left for you;
/// <see cref="ToolSpec.Validate"/> then lists exactly those gaps ("field affects nothing").
/// Snapshot the old form (<see cref="AiFgfSnapshot.Of"/>) and compare it with the new one
/// (<see cref="AiFgfLayoutSnapshot.Compare"/>, <see cref="AiFgfSnapshotTolerance.ContentOnly"/>) to see that no
/// caption or input was lost. <c>AI_FGF.Check draft &lt;assembly&gt; &lt;form type&gt;</c> does all of it.
/// </summary>
public static class AiFgfMigration
{
    private static readonly string[] BrowseWords = { "browse", "обзор", "выбрать", "…", "..." };
    private static readonly string[] FolderWords = { "folder", "directory", "dir", "папк", "каталог", "директор" };
    private static readonly string[] RunWords = { "run", "start", "go", "execute", "ok", "запуск", "выполнить", "старт" };

    public static ToolSpec DraftSpec(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var controls = All(form).Where(c => c.Visible).ToList();
        Rectangle At(Control c) => form.RectangleToClient(c.Parent is { } p ? p.RectangleToScreen(c.Bounds) : c.Bounds);
        var labels = controls.OfType<Label>().Where(l => l.Text.Trim().Length > 0).Select(l => (Label: l, At: At(l))).ToList();
        var buttons = controls.OfType<Button>().Select(b => (Button: b, At: At(b))).ToList();
        var inputs = controls.Where(IsInput).Select(c => (Control: c, At: At(c)))
                             .OrderBy(i => i.At.Y / Math.Max(1, form.Font.Height)).ThenBy(i => i.At.X).ToList();

        var spec = new ToolSpec { Title = form.Text, Executable = "" };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedLabels = new HashSet<Label>();
        foreach (var (c, at) in inputs)
        {
            string caption = c is CheckBox cb ? cb.Text : LabelFor(at) ?? c.Name;
            var field = new FieldSpec { Label = Clean(caption), Key = UniqueKey(Clean(caption), used) };
            switch (c)
            {
                case CheckBox box:
                    field.Kind = FieldKind.Flag;
                    if (box.Checked) field.Default = "true";
                    break;
                case NumericUpDown n:
                    field.Kind = FieldKind.Number;
                    (field.Min, field.Max, field.Decimals) = (n.Minimum, n.Maximum, n.DecimalPlaces);
                    field.Default = n.Value.ToString(CultureInfo.InvariantCulture);
                    break;
                case ComboBox combo:
                    field.Kind = FieldKind.Choice;
                    field.Choices = combo.Items.Cast<object>().Select(i => combo.GetItemText(i) ?? "").Where(s => s.Length > 0).ToList();
                    field.Editable = combo.DropDownStyle != ComboBoxStyle.DropDownList;
                    if (combo.Text.Length > 0) field.Default = combo.Text;
                    break;
                case TextBox { Multiline: true } memo:
                    field.Kind = FieldKind.Memo;
                    field.Lines = Math.Max(2, memo.ClientSize.Height / Math.Max(1, memo.Font.Height));
                    field.ReadOnly = memo.ReadOnly;
                    if (memo.Text.Length > 0) field.Default = memo.Text;
                    break;
                case TextBoxBase text:
                    field.Kind = BrowseNextTo(at) is { } browse
                        ? (FolderWords.Any(w => (caption + " " + browse.Text).Contains(w, StringComparison.OrdinalIgnoreCase)) ? FieldKind.Folder : FieldKind.File)
                        : FieldKind.Text;
                    if (text.Text.Length > 0) field.Default = text.Text;
                    if (text is TextBox { PlaceholderText.Length: > 0 } tb) field.Placeholder = tb.PlaceholderText;
                    break;
            }
            spec.Fields.Add(field);
        }
        if (buttons.Select(b => b.Button.Text).FirstOrDefault(t => RunWords.Any(w => AiFgfLayoutSnapshot.Plain(t, '&').Trim().Equals(w, StringComparison.OrdinalIgnoreCase))) is { } run)
            spec.RunButton = run;
        return spec;

        // the label left of the input on its row, else the one just above it (a stacked layout)
        string? LabelFor(Rectangle input)
        {
            var row = labels.Where(l => !usedLabels.Contains(l.Label) && l.At.Right <= input.Left + 4
                                        && l.At.Top < input.Bottom && l.At.Bottom > input.Top)
                            .OrderBy(l => input.Left - l.At.Right).FirstOrDefault();
            if (row.Label is null)
                row = labels.Where(l => !usedLabels.Contains(l.Label) && l.At.Bottom <= input.Top + 4 && input.Top - l.At.Bottom < 2 * form.Font.Height
                                        && l.At.Left < input.Right && l.At.Right > input.Left)
                            .OrderBy(l => input.Top - l.At.Bottom).FirstOrDefault();
            if (row.Label is null) return null;
            usedLabels.Add(row.Label);
            return row.Label.Text;
        }

        Button? BrowseNextTo(Rectangle input)
            => buttons.Where(b => b.At.Left >= input.Right - 4 && b.At.Left - input.Right < 4 * form.Font.Height
                                  && b.At.Top < input.Bottom && b.At.Bottom > input.Top
                                  && BrowseWords.Any(w => b.Button.Text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                      .Select(b => b.Button).FirstOrDefault();
    }

    private static bool IsInput(Control c) => c switch
    {
        RichTextBox => false,                         // an output log, not an input
        TextBox { ReadOnly: true, Multiline: false } => false,
        TextBoxBase or NumericUpDown or ComboBox or CheckBox => c.Parent is not NumericUpDown,
        _ => false,
    };

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            if (c is not NumericUpDown) foreach (Control d in All(c)) yield return d;
        }
    }

    /// <summary>"&amp;Source folder: *" → "Source folder".</summary>
    private static string Clean(string caption) => AiFgfLayoutSnapshot.Plain(caption, '&').Trim().TrimEnd(':', '*', ' ').Trim();

    private static string UniqueKey(string caption, HashSet<string> used)
    {
        string key = new string(caption.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim();
        key = string.Concat(key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        if (key.Length == 0) key = "Field";
        string unique = key;
        for (int i = 2; !used.Add(unique); i++) unique = key + i.ToString(CultureInfo.InvariantCulture);
        return unique;
    }
}
