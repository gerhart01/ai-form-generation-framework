using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// Moving an existing hand-made form to AI_FGF. <see cref="DraftSpec"/> reads a shown form — its
/// labels, text boxes, Browse buttons, spinners, drop-downs, check boxes, radio groups, track bars, date pickers and
/// list boxes, on every tab page — into a <see cref="ToolSpec"/> draft: one field per input, in reading order, with
/// the label, kind, current value as default, range, choices. What a form cannot tell (the executable, each field's
/// <c>arg</c>) is left for you; <see cref="ToolSpec.Validate"/> then lists exactly those gaps ("field affects nothing").
/// Snapshot the old form (<see cref="AiFgfSnapshot.Of(Form, bool)"/>, every tab page) and compare it with the new one
/// (<see cref="AiFgfLayoutSnapshot.Compare"/>, <see cref="AiFgfSnapshotTolerance.ContentOnly"/>) to see that no
/// caption or input was lost. <c>AI_FGF.Check draft &lt;assembly&gt; &lt;form type&gt;</c> does all of it.
/// </summary>
public static class AiFgfMigration
{
    private static readonly string[] BrowseWords = { "browse", "обзор", "выбрать", "…", "..." };
    private static readonly string[] FolderWords = { "folder", "directory", "dir", "папк", "каталог", "директор" };
    private static readonly string[] RunWords = { "run", "start", "go", "execute", "launch", "ok", "запуск", "запустить", "выполнить", "старт", "пуск" };

    // the old form's own window buttons: the tool form has Run, Cancel (of a run) and the close box instead
    private static readonly string[] WindowButtonWords = { "ok", "cancel", "close", "exit", "quit", "done", "отмена", "закрыть", "выход", "готово" };

    /// <summary>
    /// True for an element of the old form a tool form replaces by its own, so its absence is expected, not lost: a group
    /// box (the tool form groups its parameters itself; what the group held is compared on its own), a tab header (the
    /// tool form lists every page's fields in one column) and the old form's window buttons (OK, Cancel, Close, Exit… —
    /// the tool form has Run, Cancel and the close box). Takes a "missing: …" line of <see cref="AiFgfLayoutSnapshot.Compare"/>.
    /// </summary>
    public static bool IsReplacedByToolForm(string missing)
    {
        var m = System.Text.RegularExpressions.Regex.Match(missing, @"^missing:\s+(\w+)\s+'([^']*)'");
        if (!m.Success) return false;
        string kind = m.Groups[1].Value, caption = AiFgfLayoutSnapshot.Plain(m.Groups[2].Value, '&').Trim().TrimEnd('.', '…').Trim();
        if (kind is "Group" or "Tab") return true;
        return kind is "Button" && WindowButtonWords.Any(w => caption.Equals(w, StringComparison.OrdinalIgnoreCase));
    }

    public static ToolSpec DraftSpec(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var spec = new ToolSpec { Title = form.Text, Executable = "" };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buttonsSeen = new List<(Button Button, Rectangle At)>();
        Rectangle At(Control c) => form.RectangleToClient(c.Parent is { } p ? p.RectangleToScreen(c.Bounds) : c.Bounds);

        // every tab page is read, not only the selected one: the controls of a page that is not shown are not Visible,
        // and were left out without a word (the snapshot did not see them either). Each page is selected in turn.
        var restore = Descendants(form).OfType<TabControl>().Select(t => (Tabs: t, Index: t.SelectedIndex)).ToList();
        var browseButtons = new HashSet<Button>();
        try { spec.Fields.AddRange(ReadScope(form)); }
        finally { foreach (var (tabs, index) in restore) if (index >= 0 && index < tabs.TabCount) tabs.SelectedIndex = index; }

        // the run button: the form's AcceptButton (what Enter presses) first; else a button with a run word, the lowest one -
        // a form's own action row is at the bottom, and a "Start" inside a group (a service, a timer) is not the main action
        string? run = form.AcceptButton is Button accept && !string.IsNullOrWhiteSpace(accept.Text) ? accept.Text
            : buttonsSeen.Where(b => RunWords.Any(w => AiFgfLayoutSnapshot.Plain(b.Button.Text, '&').Trim().Equals(w, StringComparison.OrdinalIgnoreCase)))
                         .OrderByDescending(b => b.At.Bottom).ThenByDescending(b => b.At.Right).Select(b => b.Button.Text).FirstOrDefault();
        if (run is not null) spec.RunButton = run;
#if NET11_0_OR_GREATER
        // every other button - not the run button, not a Browse… beside a box, not a window button (OK, Close…) - is an
        // action of its own ("Stop" beside "Start", "Test connection"): a button beside Run, its command to fill in
        var actions = buttonsSeen.Where(b => b.Button.Text.Trim().Length > 0 && b.Button.Text != run && !browseButtons.Contains(b.Button)
                                             && !IsReplacedByToolForm($"missing: Button '{b.Button.Text}'"))
                                 .Select(b => b.Button.Text).Distinct().Select(t => new ActionSpec { Label = t }).ToList();
        if (actions.Count > 0) spec.Actions = actions;
#endif
        return spec;

        // one scope: the form outside its tab pages, or one tab page. Its inputs, radio groups and tab controls in reading
        // order; a tab control expands, where it stands, into its pages one after another.
        List<FieldSpec> ReadScope(Control scope)
        {
            var found = new List<(Rectangle At, List<FieldSpec> Fields)>();
            var controls = InScope(scope).Where(c => c.Visible).ToList();
            var labels = controls.OfType<Label>().Where(l => l.Text.Trim().Length > 0).Select(l => (Label: l, At: At(l))).ToList();
            var buttons = controls.OfType<Button>().Select(b => (Button: b, At: At(b))).ToList();
            buttonsSeen.AddRange(buttons);
            var usedLabels = new HashSet<Label>();
            var radioGroups = controls.OfType<RadioButton>().GroupBy(r => r.Parent).Select(g => (Control)g.First());
            var items = controls.Where(c => IsInput(c) || c is TabControl).Concat(radioGroups)
                                .Select(c => (Control: c, At: At(c)))
                                .OrderBy(i => i.At.Y / Math.Max(1, form.Font.Height)).ThenBy(i => i.At.X).ToList();
            foreach (var (c, at) in items)
            {
                if (c is TabControl tabs)
                {
                    var pages = new List<FieldSpec>();
                    for (int i = 0; i < tabs.TabCount; i++)
                    {
                        tabs.SelectedIndex = i;
                        pages.AddRange(ReadScope(tabs.TabPages[i]));
                    }
                    found.Add((at, pages));
                    continue;
                }
                if (c is RadioButton first)
                {
                    // the radio buttons that share a parent are one choice: its caption is their group box's, or the label beside the first
                    var group = first.Parent!.Controls.OfType<RadioButton>().Where(r => r.Visible).OrderBy(r => At(r).Y / Math.Max(1, form.Font.Height)).ThenBy(r => At(r).X).ToList();
                    string caption = first.Parent is GroupBox { Text.Length: > 0 } box ? box.Text : LabelFor(at) ?? "Choice";
                    var choice = NewField(caption, FieldKind.Choice);
                    choice.Radio = true;
                    choice.Choices = group.Select(r => Clean(r.Text)).ToList();
                    if (group.FirstOrDefault(r => r.Checked) is { } on) choice.Default = Clean(on.Text);
                    found.Add((at, new() { choice }));
                    continue;
                }
                if (c is CheckedListBox list)
                {
                    // each item a flag of its own
                    var flags = new List<FieldSpec>();
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        var flag = NewField(list.GetItemText(list.Items[i]) ?? $"Item {i + 1}", FieldKind.Flag);
                        if (list.GetItemChecked(i)) flag.Default = "true";
                        flags.Add(flag);
                    }
                    found.Add((at, flags));
                    continue;
                }
                string label = c is CheckBox cb ? cb.Text : LabelFor(at) ?? GroupCaption(c) ?? c.Name;
                var field = NewField(label, FieldKind.Text);
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
                    case TrackBar bar:
                        field.Kind = FieldKind.Number;
                        (field.Min, field.Max) = (bar.Minimum, bar.Maximum);
                        field.Default = bar.Value.ToString(CultureInfo.InvariantCulture);
                        break;
                    case DateTimePicker picker:
                        // a date (or a time) as text the tool can parse, checked by a pattern
                        bool time = picker.Format == DateTimePickerFormat.Time;
                        field.Default = picker.Value.ToString(time ? "HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
                        field.Placeholder = time ? "HH:mm" : "yyyy-MM-dd";
                        field.Pattern = time ? @"^\d{2}:\d{2}$" : @"^\d{4}-\d{2}-\d{2}$";
                        field.PatternMessage = $"{field.Label}: {field.Placeholder}";
                        break;
                    case ListBox box:
                        field.Kind = FieldKind.Choice;
                        field.Choices = box.Items.Cast<object>().Select(i => box.GetItemText(i) ?? "").Where(s => s.Length > 0).ToList();
                        if (box.SelectedItem is { } sel) field.Default = box.GetItemText(sel);
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
                    case TextBox { UseSystemPasswordChar: true } or TextBox { PasswordChar: not '\0' }:
                        // a password: hidden input, and its value is never copied into the spec (it was, in plain text)
                        field.Password = true;
                        break;
                    case TextBoxBase text:
                        Button? browse = BrowseNextTo(at);
                        bool folderWords = FolderWords.Any(w => (label + " " + browse?.Text).Contains(w, StringComparison.OrdinalIgnoreCase));
                        // a Browse button beside it makes it a file or folder; without one, a folder caption over a full path does
                        field.Kind = browse is not null ? (folderWords ? FieldKind.Folder : FieldKind.File)
                                   : folderWords && IsFullPath(text.Text) ? FieldKind.Folder : FieldKind.Text;
                        if (text.Text.Length > 0) field.Default = text.Text;
                        if (text is TextBox { PlaceholderText.Length: > 0 } tb) field.Placeholder = tb.PlaceholderText;
                        break;
                }
                found.Add((at, new() { field }));
            }
#if NET11_0_OR_GREATER
            // a label that captions no input - a hint, a status line such as "Ready." - is a note where it stood (it was
            // reported as lost, the draft failing); a group's caption is not a label
            foreach (var (l, at) in labels.Where(l => !usedLabels.Contains(l.Label)))
            {
                var note = NewField(AiFgfLayoutSnapshot.Plain(l.Text, l.UseMnemonic ? '&' : '\0').Trim(), FieldKind.Note);
                note.Label = l.UseMnemonic ? AiFgfLayoutSnapshot.Plain(l.Text, '&').Trim() : l.Text.Trim();
                found.Add((at, new() { note }));
            }
#endif
            return found.OrderBy(f => f.At.Y / Math.Max(1, form.Font.Height)).ThenBy(f => f.At.X).SelectMany(f => f.Fields).ToList();

            // the label left of the input on its row, else the one just above it (a stacked layout)
            string? LabelFor(Rectangle input)
            {
                // left of it - an auto-sized label of a form made at 96 DPI may overlap its input a little at a higher DPI
                // ("Environment:" ran 2 px into its list box at 125 % and was not found)
                var row = labels.Where(l => !usedLabels.Contains(l.Label) && l.At.Left < input.Left && l.At.Right <= input.Left + Math.Max(4, form.Font.Height)
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
            {
                Button? b = buttons.Where(b => b.At.Left >= input.Right - 4 && b.At.Left - input.Right < 4 * form.Font.Height
                                               && b.At.Top < input.Bottom && b.At.Bottom > input.Top
                                               && BrowseWords.Any(w => b.Button.Text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                                   .Select(b => b.Button).FirstOrDefault();
                if (b is not null) browseButtons.Add(b);
                return b;
            }
        }

        FieldSpec NewField(string caption, FieldKind kind) => new() { Label = Clean(caption), Key = UniqueKey(Clean(caption), used), Kind = kind };
    }

#if NET11_0_OR_GREATER
    /// <summary>
    /// .NET 11: an instance of an old form to draft from, whatever its constructor. A parameterless one is called; a form
    /// that takes arguments (a service, a document, a settings object) is built without its constructor: the Form base is
    /// initialised and the Designer's <c>InitializeComponent()</c> called - which is what makes its controls. Such a form
    /// used to be refused ("no parameterless constructor"), leaving only a hand migration.
    /// </summary>
    public static Form CreateForm(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!typeof(Form).IsAssignableFrom(type) || type.IsAbstract) throw new ArgumentException($"{type.FullName} is not a form that can be made", nameof(type));
        if (type.GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, Type.EmptyTypes) is { } plain)
            return (Form)plain.Invoke(null);
        var init = type.GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, Type.EmptyTypes)
            ?? throw new MissingMethodException($"{type.FullName} has no parameterless constructor and no InitializeComponent() to make its controls");
        var form = (Form)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        // run the constructors of the framework's own bases (Form and up) on it: the window and control state they set up
        typeof(Form).GetConstructor(Type.EmptyTypes)!.Invoke(form, null);
        init.Invoke(form, null);
        return form;
    }
#endif

    // a box with no label of its own, alone in a captioned group box: the group's caption names it (it was the control's
    // Name - "textBox3" - or empty)
    private static string? GroupCaption(Control c)
        => c.Parent is GroupBox { Text.Length: > 0 } g && g.Controls.Cast<Control>().Count(IsInput) == 1 ? g.Text : null;

    private static bool IsFullPath(string s)
    {
        try { return s.Length > 2 && Path.IsPathFullyQualified(s); }
        catch (ArgumentException) { return false; }
    }

    private static bool IsInput(Control c) => c switch
    {
        RichTextBox => false,                         // an output log, not an input
        TextBox { ReadOnly: true, Multiline: false } => false,
        TextBox { ReadOnly: true, Multiline: true, Text.Length: 0 } => false,   // an empty read-only box: the old form's output log
        TextBoxBase or NumericUpDown or ComboBox or CheckBox or TrackBar or DateTimePicker or ListBox => c.Parent is not NumericUpDown,
        _ => false,
    };

    /// <summary>The controls of a scope, not looking into a tab control (its pages are scopes of their own) or a spinner.</summary>
    private static IEnumerable<Control> InScope(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            if (c is not (NumericUpDown or TabControl)) foreach (Control d in InScope(c)) yield return d;
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control d in Descendants(c)) yield return d;
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
