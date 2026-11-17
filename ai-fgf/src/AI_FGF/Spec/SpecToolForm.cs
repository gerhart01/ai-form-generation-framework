using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// A complete <see cref="ToolForm"/> built from a <see cref="ToolSpec"/> — no UI code: rows for
/// every field (with defaults, required marks and pattern rules), the command line from the
/// field values, presets/timeout/busy overlay from the spec, and an optional results CSV
/// loaded into the Results tab after a successful run.
/// </summary>
public sealed class SpecToolForm(ToolSpec spec) : ToolForm
{
    // Primary-constructor field initializers run before the ToolForm constructor, which calls
    // BuildParameters and reads the virtual properties below — so the spec is already set.
    private readonly ToolSpec _spec = spec ?? throw new ArgumentNullException(nameof(spec));

    /// <summary>The spec this form was built from.</summary>
    public ToolSpec Spec => _spec;

    protected override string FormTitle => _spec.Title;
    protected override string RunButtonText => string.IsNullOrWhiteSpace(_spec.RunButton) ? "▶ Run" : _spec.RunButton;
    protected override bool PresetButtons => _spec.Presets;
    protected override bool ShowBusyOverlay => _spec.BusyOverlay;
    protected override bool ThemeButton => _spec.ThemeButton;
    protected override AiFgfButtonStyle ButtonStyle => _spec.ButtonStyle ?? base.ButtonStyle;
    protected override bool SunkenFields => _spec.SunkenFields;

    protected override void OnLoad(EventArgs e)
    {
        // the spec's colours first; a choice the user saved (ThemeButton) is restored after this
        if (_spec.ThemeChoice() is { } choice && !choice.Equals(AiFgfTheme.Current.Choice)) AiFgfTheme.Switch(choice.ToTheme(), this);
        base.OnLoad(e);
    }
    protected override TimeSpan? RunTimeout => _spec.TimeoutSeconds is double s && s > 0 ? TimeSpan.FromSeconds(s) : null;
    protected override string? SettingsKey => _spec.SettingsKey switch
    {
        "" => null,   // "settingsKey": "" disables persistence
        null => "spec." + string.Concat(_spec.Title.Where(char.IsLetterOrDigit)),
        var key => key,
    };

    protected override void BuildParameters(FormScaffold.RowBuilder rows)
    {
        var flags = new List<(string Key, CheckBox Box)>();
        void FlushFlags()
        {
            if (flags.Count == 0) return;
            rows.Checks(flags.ToArray());
            flags.Clear();
        }

        foreach (FieldSpec f in _spec.Fields)
        {
            if (f.Kind == FieldKind.Flag)
            {
                // consecutive flags share one wrapping row of checkboxes
                bool on = bool.TryParse(f.Default, out bool d) && d;
#if NET11_0_OR_GREATER
                flags.Add((f.Key, f.Switch ? AiFgf.Toggle(f.Label, on) : AiFgf.Check(f.Label, on)));   // "switch": true - an on/off switch
#else
                flags.Add((f.Key, AiFgf.Check(f.Label, on)));
#endif
                continue;
            }
            FlushFlags();
            string label = f.Label.TrimEnd(':') + ":";
            switch (f.Kind)
            {
                case FieldKind.Text:
                    if (f.ChoicesFromMarker is { Length: > 0 } textMarker)
                    {
                        // a text field that discovers its own choices: an editable type-ahead combo
                        rows.Combo(label, out ComboBox textCombo, editable: true, f.Required, f.Key);
                        if (f.Default is not null) textCombo.Text = f.Default;
                        FillComboFromMarker(textMarker, textCombo);
                    }
                    else
                    {
                        TextBox text;
#if NET11_0_OR_GREATER
                        if (f.Password) rows.Password(label, out text, f.Placeholder, f.Required, f.Key);   // "password": true - hidden input
                        else
#endif
                        rows.Text(label, out text, f.Placeholder, f.Required, f.Key);
                        if (f.Default is not null) text.Text = f.Default;
                    }
                    break;
                case FieldKind.Memo:
                    rows.Memo(label, out AiFgfMemo memo, f.Lines ?? 4, f.Default, f.Required, f.Key, f.MaxLines ?? Math.Max(12, f.Lines ?? 4), f.ReadOnly);
                    if (f.Placeholder is not null) memo.PlaceholderText = f.Placeholder;
                    break;
                case FieldKind.File:
                    rows.FileText(label, out TextBox file, f.Filter ?? "All files|*.*", f.Label, f.Required, f.Key);
                    if (f.Placeholder is not null) file.PlaceholderText = f.Placeholder;
                    if (f.Default is not null) file.Text = f.Default;
                    break;
                case FieldKind.Folder:
                    rows.FolderText(label, out TextBox folder, f.Label, f.Required, f.Key);
                    if (f.Placeholder is not null) folder.PlaceholderText = f.Placeholder;
                    if (f.Default is not null) folder.Text = f.Default;
                    break;
                case FieldKind.Choice:
#if NET11_0_OR_GREATER
                    if (f.Radio && f.Choices is { Count: > 0 } radios)   // "radio": true - every choice at once
                    {
                        rows.Radios(label, radios, f.Default is { } sel && radios.Contains(sel) ? sel : radios[0], f.Key);
                        break;
                    }
#endif
                    rows.Combo(label, out ComboBox combo, f.Editable, f.Required, f.Key);
                    if (f.Choices is not null) combo.Items.AddRange(f.Choices.Cast<object>().ToArray());
                    if (f.Default is not null) combo.Text = f.Default;
                    else if (!f.Editable && combo.Items.Count > 0) combo.SelectedIndex = 0;
                    if (f.ChoicesFromMarker is { Length: > 0 } choiceMarker) FillComboFromMarker(choiceMarker, combo);
                    break;
#if NET11_0_OR_GREATER
                case FieldKind.Note:
                    // a line of text across the form (a hint, a status line carried over from an old form)
                    rows.Custom("", AiFgf.Note(f.Label));   // a plate in the 3D looks
                    break;
#endif
                case FieldKind.Number:
                    decimal min = f.Min ?? 0, max = f.Max ?? 100;
                    decimal value = decimal.TryParse(f.Default, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) ? d : min;
                    rows.Number(label, out _, min, max, value, f.Decimals, f.Key);
                    break;
            }
            if (f.Pattern is { } pattern)
            {
                var regex = new Regex(pattern);
                string message = f.PatternMessage ?? $"{f.Label} has an invalid format.";
                rows.Fields.Rule(f.Key, v => v.Length == 0 || regex.IsMatch(v) ? null : message);
            }
        }
        FlushFlags();

        // Conditions last, once every field they may refer to exists.
        foreach (FieldSpec f in _spec.Fields)
        {
            if (f.EnabledWhen is { } en && FieldCondition.TryParse(en, out FieldCondition? enc, out _))
                rows.Fields.EnableWhen(f.Key, fs => enc!.Evaluate(fs.GetActiveValues()));
            if (f.VisibleWhen is { } vi && FieldCondition.TryParse(vi, out FieldCondition? vic, out _))
                rows.Fields.VisibleWhen(f.Key, fs => vic!.Evaluate(fs.GetActiveValues()));
        }
    }

    protected override ProcessRunner? BuildRun() => _spec.BuildRunner(Fields.GetValues());
#if NET11_0_OR_GREATER

    // "actions": a button each beside Run - its own command from the same fields, run by the same controller
    protected override void ConfigureButtons(FormScaffold.BarBuilder bar)
    {
        base.ConfigureButtons(bar);
        foreach (ActionSpec action in _spec.Actions ?? new())
        {
            ActionSpec a = action;
            bar.Button(a.Label, (_, _) => StartRun(() => _spec.BuildRunner(Fields.GetValues(), a)), out var button);
            AddActionButton(button);
        }
    }
#endif

    protected override void OnRunFinished(ProcessResult result)
    {
        if (!result.Success || _spec.ResultsCsv is not { Length: > 0 } template) return;
        string path = _spec.Resolve(template, Fields.GetValues());
        if (!File.Exists(path)) { Log.Warn($"No results file at {path}"); return; }
        try
        {
            Results.ShowCsv(path);
            ShowResultsTab();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not read {path}: {ex.Message}");
        }
    }
}
