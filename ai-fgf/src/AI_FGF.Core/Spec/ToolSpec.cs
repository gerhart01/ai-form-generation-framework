using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AI_FGF;

/// <summary>The kind of input a <see cref="FieldSpec"/> renders as.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FieldKind>))]
public enum FieldKind { Text, File, Folder, Choice, Number, Flag, Memo }

/// <summary>One input of a <see cref="ToolSpec"/>: how it looks, how it validates, how it becomes arguments.</summary>
public sealed class FieldSpec
{
    /// <summary>Stable key (settings, presets, <c>{Key}</c> placeholders). Required, unique.</summary>
    public string Key { get; set; } = "";
    /// <summary>Caption shown next to the input. Required.</summary>
    public string Label { get; set; } = "";
    /// <summary>Visible lines of a <c>memo</c> field (default 4). Memo lines never wrap.</summary>
    public int? Lines { get; set; }
    /// <summary>A <c>memo</c> grows with its text up to this many lines before it scrolls (default 12).</summary>
    public int? MaxLines { get; set; }
    /// <summary>A <c>memo</c> that shows text instead of taking input.</summary>
    public bool ReadOnly { get; set; }
    /// <summary>The input: text, file, folder, choice, number, flag or memo.</summary>
    public FieldKind Kind { get; set; } = FieldKind.Text;
    /// <summary>The value must not be empty (shown as "Label *").</summary>
    public bool Required { get; set; }
    /// <summary>Initial value: text, a choice, a number (<c>4</c> or <c>"4"</c>) or a flag (<c>true</c>/<c>false</c>).</summary>
    [JsonConverter(typeof(ScalarAsStringConverter))]
    public string? Default { get; set; }
    /// <summary>Grey hint text shown while the field is empty.</summary>
    public string? Placeholder { get; set; }
    /// <summary>Choice: the allowed values (shown in a drop-down list).</summary>
    public List<string>? Choices { get; set; }
    /// <summary>Choice: allow typing values that are not in the list.</summary>
    public bool Editable { get; set; }
    /// <summary>
    /// Fill a type-ahead combo live from the tool's own output: each stdout line
    /// <c>&lt;marker&gt;|value</c> (e.g. <c>"##FEATURE##"</c>) adds <c>value</c> to this field's drop-down,
    /// and the marker lines are hidden from the log. Turns a <c>text</c> field into an editable combo
    /// (a <c>choice</c> keeps its listed items and gains the discovered ones). The tool must print the
    /// markers — usually gated by an <see cref="ToolSpec.Env"/> variable. Run once (e.g. with the field
    /// empty) to populate it.
    /// </summary>
    public string? ChoicesFromMarker { get; set; }
    /// <summary>Number: the smallest value.</summary>
    public decimal? Min { get; set; }
    /// <summary>Number: the largest value.</summary>
    public decimal? Max { get; set; }
    /// <summary>Number: digits after the decimal point (shown and passed).</summary>
    public int Decimals { get; set; }
    /// <summary>File: dialog filter, e.g. <c>"Logs|*.log|All files|*.*"</c>.</summary>
    public string? Filter { get; set; }
    /// <summary>Text/File/Folder/Choice: value must match this regex.</summary>
    public string? Pattern { get; set; }
    /// <summary>The message shown when the value does not match Pattern.</summary>
    public string? PatternMessage { get; set; }

    /// <summary>
    /// How the value becomes command-line arguments: <c>"-o"</c> emits <c>-o value</c> (skipped
    /// when empty); <c>"--level={value}"</c> emits one token; for a flag, the argument is emitted
    /// when checked. Leave empty and set <see cref="Positional"/> for a bare value.
    /// </summary>
    public string? Arg { get; set; }
    /// <summary>Emit the value as a bare argument, after all options, in field order.</summary>
    public bool Positional { get; set; }

    /// <summary>
    /// Enable this field only while a condition holds: <c>"Key"</c> (set / checked),
    /// <c>"!Key"</c>, <c>"Key=value"</c>, <c>"Key!=value"</c>. A disabled field is not validated
    /// and not passed to the tool.
    /// </summary>
    public string? EnabledWhen { get; set; }

    /// <summary>Show this field only while a condition holds (same syntax); hidden fields take no space.</summary>
    public string? VisibleWhen { get; set; }
}

/// <summary>A field condition: <c>Key</c>, <c>!Key</c>, <c>Key=value</c> or <c>Key!=value</c>.</summary>
public sealed record FieldCondition(string Key, bool Negate, string? Value)
{
    public static bool TryParse(string? text, out FieldCondition? condition, out string error)
    {
        condition = null; error = "";
        string t = (text ?? "").Trim();
        if (t.Length == 0) { error = "is empty"; return false; }
        int ne = t.IndexOf("!=", StringComparison.Ordinal);
        int eq = t.IndexOf('=');
        if (ne > 0) condition = new FieldCondition(t[..ne].Trim(), true, t[(ne + 2)..].Trim());
        else if (eq > 0) condition = new FieldCondition(t[..eq].Trim(), false, t[(eq + 1)..].Trim());
        else if (t.StartsWith('!')) condition = new FieldCondition(t[1..].Trim(), true, null);
        else condition = new FieldCondition(t, false, null);
        if (condition.Key.Length == 0 || condition.Key.Contains(' '))
        {
            error = $"'{t}' — use Key, !Key, Key=value or Key!=value";
            condition = null;
            return false;
        }
        return true;
    }

    /// <summary>Evaluate against field values (a missing key counts as empty).</summary>
    public bool Evaluate(IReadOnlyDictionary<string, string> values)
    {
        string v = values.TryGetValue(Key, out string? raw) ? raw?.Trim() ?? "" : "";
        bool result = Value is null
            ? v.Length > 0 && !v.Equals("false", StringComparison.OrdinalIgnoreCase) && v != "0"
            : string.Equals(v, Value, StringComparison.OrdinalIgnoreCase) || AiFgfNumbers.SameNumber(v, Value);   // "Ratio=1.5" matches "1.50"
        return Negate ? !result : result;
    }
}

/// <summary>A spec-validation finding.</summary>
public sealed record SpecIssue(bool IsError, string Where, string Message)
{
    public override string ToString() => $"{(IsError ? "error" : "warning")}: {Where}: {Message}";
}

/// <summary>
/// A declarative description of a tool form — the "template" that turns a short description
/// of a command-line tool into a correct <c>SpecToolForm</c> (WinForms or Avalonia) without writing UI code:
/// <code>
/// var spec = ToolSpec.FromFile("tool.json");
/// if (spec.Validate().Any(i => i.IsError)) …
/// AiFgfApp.Run(() => new SpecToolForm(spec));
/// </code>
/// JSON is case-insensitive and tolerates comments and trailing commas, so it can be edited by hand.
/// </summary>
public sealed class ToolSpec
{
    /// <summary>The window title. Required.</summary>
    public string Title { get; set; } = "";
    /// <summary>The program to run (on PATH or a full path). With an <see cref="Interpreter"/>, an optional override of it.</summary>
    public string Executable { get; set; } = "";

    /// <summary>
    /// Run <see cref="Script"/> with <c>"pwsh"</c> (PowerShell; Windows PowerShell if pwsh is
    /// missing) or <c>"python"</c> — UTF-8 output, no buffering, no profile/policy surprises.
    /// The field arguments (after fixedArgs) become the script's arguments.
    /// </summary>
    public string? Interpreter { get; set; }

    /// <summary>The script for <see cref="Interpreter"/>; a relative path is relative to the spec file.</summary>
    public string? Script { get; set; }

    /// <summary>The folder of the spec file (set by <see cref="FromFile"/>), for resolving <see cref="Script"/>.</summary>
    [JsonIgnore]
    public string? BaseDirectory { get; set; }
    /// <summary>Arguments always passed first (e.g. <c>["-NoProfile", "-File", "script.ps1"]</c>).</summary>
    public List<string> FixedArgs { get; set; } = new();
    /// <summary>Working directory; may use <c>{Key}</c> placeholders.</summary>
    public string? WorkingDirectory { get; set; }
    /// <summary><c>"oem"</c> (default: console code page) or <c>"utf8"</c>.</summary>
    public string Encoding { get; set; } = "oem";
    /// <summary>Stop a run (the whole process tree) after this many seconds; omit for no limit.</summary>
    public double? TimeoutSeconds { get; set; }
    /// <summary>Caption of the Run button; "&amp;" marks the Alt access key.</summary>
    public string RunButton { get; set; } = "▶ Run";
    /// <summary>Save preset… / Load preset… buttons.</summary>
    public bool Presets { get; set; } = true;
    /// <summary>Dim the window while running (hides the live log).</summary>
    public bool BusyOverlay { get; set; }

    /// <summary>The tool's colours: <c>{ "accent": "#0F7B6C", "dark": false, "tint": 0.2 }</c> (WinForms: <c>AiFgfTheme.Create</c>).</summary>
    public ThemeSpec? Theme { get; set; }

    /// <summary>Show a "Theme…" button so the user can pick colours (saved with the settings).</summary>
    public bool ThemeButton { get; set; }
    /// <summary>
    /// Draw every input field as a sunken Direct3D card with a focus-glow ring (the WinForms
    /// Direct3D look), the same effect the Results grid already gets. Best with a dark
    /// <see cref="Theme"/> and <c>"buttonStyle": "glow"</c>.
    /// </summary>
    public bool SunkenFields { get; set; }
    /// <summary>
    /// Environment variables set for the tool's process, e.g. <c>{ "MYTOOL_PROGRESS_STDOUT": "1" }</c>
    /// to make a script emit its <c>##PROGRESS##</c> / <see cref="FieldSpec.ChoicesFromMarker"/> markers.
    /// </summary>
    public Dictionary<string, string>? Env { get; set; }
    /// <summary>
    /// How buttons are drawn: "flat", "raised" or "glow" (<see cref="AiFgfEffects"/>); absent = the application's
    /// default. A choice the user makes in the "Theme…" dialog wins; flat where an effect cannot be drawn.
    /// </summary>
    public AiFgfButtonStyle? ButtonStyle { get; set; }
    /// <summary>
    /// How <see cref="ButtonStyle"/> ("raised"/"glow") is actually drawn: absent (default) for the
    /// cross-platform look (WinForms: Direct3D 12; Avalonia: a drop shadow), or <c>"direct3d"</c> to ask
    /// Avalonia for the real Direct3D 12 rendering too (the WinForms look: buttons and field focus
    /// glow) — only on Windows, and only when the app referenced the experimental add-on
    /// (AI_FGF.Avalonia.Direct3D) and it could initialise; otherwise this is ignored and the drop
    /// shadow is used, unchanged. WinForms already draws with Direct3D 12 and ignores this field.
    /// </summary>
    public string? Renderer { get; set; }

    /// <summary>The <see cref="Theme"/> as a choice, or null when absent or invalid.</summary>
    public AiFgfThemeChoice? ThemeChoice()
        => Theme is { } t && AiFgfColor.TryParseHex(t.Accent, out var accent) && t.Tint is >= 0 and <= 1
            ? new AiFgfThemeChoice(accent, t.Dark, t.Tint) : null;
    /// <summary>After a successful run, load this CSV into the Results tab; may use <c>{Key}</c> placeholders.</summary>
    public string? ResultsCsv { get; set; }
    /// <summary>Exit codes that mean success: <c>"0"</c> (default), <c>"0-7"</c>, <c>"0,1"</c>, <c>"0-3, 8"</c>.</summary>
    public string? SuccessCodes { get; set; }
    /// <summary>Exit codes that mean "finished with a warning", with a message: <c>{ "3010": "Reboot required" }</c>.</summary>
    public Dictionary<string, string>? WarningCodes { get; set; }
    /// <summary>Readable messages for failing exit codes: <c>{ "16": "Fatal error" }</c>.</summary>
    public Dictionary<string, string>? FailureMessages { get; set; }

    /// <summary>Key under which settings are saved (default: derived from the title; <c>""</c> = do not persist).</summary>
    public string? SettingsKey { get; set; }
    /// <summary>The inputs, in the order shown; each becomes arguments as its arg / positional say.</summary>
    public List<FieldSpec> Fields { get; set; } = new();

    // ------------------------------------------------------------------ load

    /// <summary>How a spec is read and written (and what <see cref="ToolSpecSchema"/> describes): camelCase names, lowercase kinds.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter<FieldKind>(JsonNamingPolicy.CamelCase), new JsonStringEnumConverter<AiFgfButtonStyle>(JsonNamingPolicy.CamelCase) },   // "text", "file"… (read in any case)
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // "▶ &Run", Cyrillic: as written
    };

    private static JsonSerializerOptions Json => JsonOptions;

    /// <summary>Parse a spec; malformed JSON throws <see cref="JsonException"/> with line/position.</summary>
    public static ToolSpec Parse(string json) => JsonSerializer.Deserialize<ToolSpec>(json, Json) ?? new ToolSpec();

    public static ToolSpec FromFile(string path)
    {
        ToolSpec spec = Parse(File.ReadAllText(path));
        spec.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
        return spec;
    }

    /// <summary>The script path, resolved against <see cref="BaseDirectory"/> when relative.</summary>
    public string? ResolvedScript
        => Script is null ? null : Path.IsPathRooted(Script) || BaseDirectory is null ? Script : Path.Combine(BaseDirectory, Script);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The spec with only what differs from the defaults — what a person would write (drafts, generated specs).</summary>
    public string ToCompactJson()
    {
        var spec = (System.Text.Json.Nodes.JsonObject)JsonSerializer.SerializeToNode(this, Json)!;
        Strip(spec, JsonSerializer.SerializeToNode(new ToolSpec(), Json)!.AsObject());
        var fieldDefaults = JsonSerializer.SerializeToNode(new FieldSpec(), Json)!.AsObject();
        fieldDefaults.Remove("key"); fieldDefaults.Remove("label");
        foreach (var field in spec["fields"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray())
            if (field is System.Text.Json.Nodes.JsonObject f) Strip(f, fieldDefaults);
        return spec.ToJsonString(Json);

        static void Strip(System.Text.Json.Nodes.JsonObject obj, System.Text.Json.Nodes.JsonObject defaults)
        {
            foreach (var (name, value) in defaults)
                if (name != "fields" && name != "title" && name != "executable" && obj[name] is { } mine
                    && System.Text.Json.Nodes.JsonNode.DeepEquals(mine, value))
                    obj.Remove(name);
        }
    }

    // -------------------------------------------------------------- validate

    private static readonly Regex Placeholder = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Everything that would make the form wrong or the command line nonsensical. Errors must be
    /// fixed; warnings point at things that are probably mistakes (e.g. a field that affects nothing).
    /// </summary>
    public IReadOnlyList<SpecIssue> Validate()
    {
        var issues = new List<SpecIssue>();
        void Err(string where, string msg) => issues.Add(new SpecIssue(true, where, msg));
        void Warn(string where, string msg) => issues.Add(new SpecIssue(false, where, msg));

        if (string.IsNullOrWhiteSpace(Title)) Err("title", "is empty");
        if (Theme is { } theme)
        {
            if (!AiFgfColor.TryParseHex(theme.Accent, out _)) Err("theme.accent", $"'{theme.Accent}' is not a colour — use \"#RRGGBB\"");
            if (theme.Tint is < 0 or > 1) Err("theme.tint", $"{theme.Tint} must be 0–1");
        }
        if (Renderer is not null && !Renderer.Equals("direct3d", StringComparison.OrdinalIgnoreCase))
            Err("renderer", $"'{Renderer}' — the only value is \"direct3d\" (omit it for the default look)");
        if (Renderer is not null && ButtonStyle is AiFgfButtonStyle.Flat)
            Warn("renderer", "\"direct3d\" changes nothing while buttonStyle is \"flat\"");
        if (Interpreter is null && string.IsNullOrWhiteSpace(Executable)) Err("executable", "is empty (or set \"interpreter\" and \"script\")");
        if (Interpreter is not null)
        {
            if (!new[] { "pwsh", "powershell", "python" }.Contains(Interpreter, StringComparer.OrdinalIgnoreCase))
                Err("interpreter", $"'{Interpreter}' — use \"pwsh\" or \"python\"");
            if (string.IsNullOrWhiteSpace(Script)) Err("script", "is required with an interpreter");
            else if (BaseDirectory is not null && !File.Exists(ResolvedScript)) Warn("script", $"'{ResolvedScript}' does not exist (yet)");
        }
        else if (Script is not null) Err("script", "needs \"interpreter\": \"pwsh\" or \"python\"");
        if (!Encoding.Equals("oem", StringComparison.OrdinalIgnoreCase) && !Encoding.Equals("utf8", StringComparison.OrdinalIgnoreCase))
            Err("encoding", $"'{Encoding}' — use \"oem\" or \"utf8\"");
        if (TimeoutSeconds is <= 0) Err("timeoutSeconds", "must be positive (omit it for no limit)");
        if (Fields.Count == 0) Warn("fields", "no fields — the form will only have a Run button");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Fields.Count; i++)
        {
            FieldSpec f = Fields[i];
            string where = $"fields[{i}]" + (string.IsNullOrWhiteSpace(f.Key) ? "" : $" '{f.Key}'");
            if (string.IsNullOrWhiteSpace(f.Key)) Err(where, "key is empty");
            else if (!keys.Add(f.Key)) Err(where, "duplicate key (keys are case-insensitive)");
            if (string.IsNullOrWhiteSpace(f.Label)) Err(where, "label is empty");

            switch (f.Kind)
            {
                case FieldKind.Choice:
                    if (f.Choices is not { Count: > 0 }) Err(where, "a choice needs \"choices\"");
                    else if (f.Default is not null && !f.Editable && !f.Choices.Contains(f.Default))
                        Err(where, $"default '{f.Default}' is not one of the choices");
                    break;
                case FieldKind.Number:
                    decimal min = f.Min ?? 0, max = f.Max ?? 100;
                    if (min > max) Err(where, $"min {min} > max {max}");
                    if (f.Decimals is < 0 or > 10) Err(where, "decimals must be 0–10");
                    if (f.Default is not null)
                    {
                        if (!decimal.TryParse(f.Default, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d))
                            Err(where, $"default '{f.Default}' is not a number (use '.' as the decimal separator)");
                        else if (d < min || d > max) Err(where, $"default {d} is outside {min}–{max}");
                    }
                    break;
                case FieldKind.Flag:
                    if (f.Positional) Err(where, "a flag cannot be positional");
                    if (f.Required) Err(where, "a flag cannot be required");
                    if (f.Default is not null && !bool.TryParse(f.Default, out _)) Err(where, $"default '{f.Default}' must be true or false");
                    break;
                case FieldKind.Memo:
                    if (f.Lines is < 1 or > 40) Err(where, "lines must be 1–40");
                    if (f.MaxLines is { } ml && (ml < (f.Lines ?? 4) || ml > 200)) Err(where, $"maxLines must be {f.Lines ?? 4}–200");
                    break;
                case FieldKind.File:
                    if (f.Filter is { } filter && filter.Split('|').Length % 2 != 0)
                        Err(where, $"filter '{filter}' must be pairs: \"Description|*.ext|…\"");
                    break;
            }
            if (f.Kind is FieldKind.Number or FieldKind.Flag && f.Pattern is not null) Err(where, $"a pattern does not apply to a {f.Kind.ToString().ToLowerInvariant()}");
            if (f.Pattern is { } p)
            {
                try { _ = new Regex(p); }
                catch (ArgumentException ex) { Err(where, $"pattern is not a valid regex: {ex.Message}"); }
            }
            if (f.ChoicesFromMarker is { } marker)
            {
                if (string.IsNullOrWhiteSpace(marker)) Err(where, "choicesFromMarker is empty");
                else if (f.Kind is not (FieldKind.Text or FieldKind.Choice)) Err(where, $"choicesFromMarker applies to a text or choice field, not a {f.Kind.ToString().ToLowerInvariant()}");
                else if (Env is null or { Count: 0 }) Warn(where, $"choicesFromMarker '{marker}' needs the tool to print those lines — usually enabled by an \"env\" variable");
            }
            if (f.Positional && !string.IsNullOrEmpty(f.Arg)) Err(where, "set either \"arg\" or \"positional\", not both");
            if (f.Arg is { } arg && !arg.Contains("{value}", StringComparison.Ordinal) && arg.Contains(' '))
                Warn(where, $"arg '{arg}' contains a space — it is passed as ONE argument; use \"--name={{value}}\" or split it");
            if (f.Kind == FieldKind.Flag && string.IsNullOrEmpty(f.Arg))
                Warn(where, "a flag without \"arg\" changes nothing on the command line");
            if (f.Kind != FieldKind.Flag && string.IsNullOrEmpty(f.Arg) && !f.Positional && !IsReferenced(f.Key))
                Warn(where, "not passed to the tool (no \"arg\", not positional, not used in workingDirectory/resultsCsv)");
        }

        for (int i = 0; i < Fields.Count; i++)
        {
            FieldSpec f = Fields[i];
            foreach (var (prop, text) in new[] { ("enabledWhen", f.EnabledWhen), ("visibleWhen", f.VisibleWhen) })
            {
                if (text is null) continue;
                string where = $"fields[{i}] '{f.Key}' {prop}";
                if (!FieldCondition.TryParse(text, out FieldCondition? c, out string error)) { Err(where, error); continue; }
                if (!keys.Contains(c!.Key)) Err(where, $"refers to unknown field '{c.Key}'");
                else if (string.Equals(c.Key, f.Key, StringComparison.OrdinalIgnoreCase)) Err(where, "a field cannot depend on itself");
            }
            if (f.Required && (f.EnabledWhen is not null || f.VisibleWhen is not null))
                Warn($"fields[{i}] '{f.Key}'", "required only applies while the field is enabled and visible");
        }

        if (SuccessCodes is not null && !ExitCodePolicy.TryParseRanges(SuccessCodes, out _, out string rangeError))
            Err("successCodes", rangeError);
        var warningCodes = new HashSet<int>();
        foreach (var (name, map) in new[] { ("warningCodes", WarningCodes), ("failureMessages", FailureMessages) })
            foreach (string code in map?.Keys ?? Enumerable.Empty<string>())
            {
                if (!int.TryParse(code, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int c)) Err(name, $"'{code}' is not an exit code");
                else if (name == "warningCodes") warningCodes.Add(c);
                else if (warningCodes.Contains(c)) Err(name, $"exit code {c} is both a warning and a failure");
            }

        foreach (var (name, text) in new[] { ("workingDirectory", WorkingDirectory), ("resultsCsv", ResultsCsv) })
            if (text is not null)
                foreach (Match m in Placeholder.Matches(text))
                    if (!keys.Contains(m.Groups[1].Value)) Err(name, $"refers to unknown field {{{m.Groups[1].Value}}}");
        return issues;
    }

    private bool IsReferenced(string key)
        => new[] { WorkingDirectory, ResultsCsv }.Any(t => t is not null && t.Contains("{" + key + "}", StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------ run mapping

    /// <summary>Replace <c>{Key}</c> placeholders with field values.</summary>
    public string Resolve(string template, IReadOnlyDictionary<string, string> values)
        => Placeholder.Replace(template, m => values.TryGetValue(m.Groups[1].Value, out string? v) ? v : m.Value);

    /// <summary>
    /// The fields that are active (enabled and visible) for these values. Conditions apply
    /// transitively: a condition naming an inactive field sees that field as empty.
    /// </summary>
    public IReadOnlySet<string> ActiveFields(IReadOnlyDictionary<string, string> values)
    {
        var active = new HashSet<string>(Fields.Select(f => f.Key), StringComparer.OrdinalIgnoreCase);
        for (int pass = 0; pass <= Fields.Count; pass++)
        {
            var visibleValues = values.Where(kv => active.Contains(kv.Key))
                                      .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FieldSpec f in Fields)
                if (Holds(f.EnabledWhen, visibleValues) && Holds(f.VisibleWhen, visibleValues)) next.Add(f.Key);
            if (next.SetEquals(active)) break;
            active = next;
        }
        return active;
    }

    private static bool Holds(string? condition, IReadOnlyDictionary<string, string> values)
        => condition is null || !FieldCondition.TryParse(condition, out FieldCondition? c, out _) || c!.Evaluate(values);

    /// <summary>The arguments for these field values: fixed args, then options in field order, then positionals.</summary>
    public IReadOnlyList<string> BuildArguments(IReadOnlyDictionary<string, string> values)
    {
        var args = new List<string>(FixedArgs);
        var positionals = new List<string>();
        IReadOnlySet<string> active = ActiveFields(values);
        foreach (FieldSpec f in Fields)
        {
            if (!active.Contains(f.Key)) continue;   // disabled or hidden: not passed
            values.TryGetValue(f.Key, out string? raw);
            string value = (raw ?? "").Trim();
            if (f.Kind == FieldKind.Flag)
            {
                if (bool.TryParse(value, out bool on) && on && !string.IsNullOrEmpty(f.Arg)) args.Add(f.Arg);
                continue;
            }
            if (value.Length == 0) continue;
            if (f.Positional) { positionals.Add(value); continue; }
            if (string.IsNullOrEmpty(f.Arg)) continue;
            if (f.Arg.Contains("{value}", StringComparison.Ordinal)) args.Add(f.Arg.Replace("{value}", value, StringComparison.Ordinal));
            else { args.Add(f.Arg); args.Add(value); }
        }
        args.AddRange(positionals);
        return args;
    }

    /// <summary>Apply this spec's successCodes/warningCodes/failureMessages to a policy (invalid entries are skipped).</summary>
    public void ApplyExitCodes(ExitCodePolicy policy)
    {
        if (SuccessCodes is not null && ExitCodePolicy.TryParseRanges(SuccessCodes, out var ranges, out _))
            foreach (var (from, to) in ranges) policy.Success(from, to);
        foreach (var (code, message) in WarningCodes ?? new())
            if (int.TryParse(code, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int c)) policy.Warning(c, message);
        foreach (var (code, message) in FailureMessages ?? new())
            if (int.TryParse(code, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int c)) policy.Failure(c, message);
    }

    /// <summary>A configured <see cref="ProcessRunner"/> for these field values.</summary>
    public ProcessRunner BuildRunner(IReadOnlyDictionary<string, string> values)
    {
        IReadOnlyList<string> args = BuildArguments(values);
        string? exe = string.IsNullOrWhiteSpace(Executable) ? null : Executable;
        ProcessRunner runner;
        switch (Interpreter?.ToLowerInvariant())
        {
            case "pwsh" or "powershell":
                runner = ProcessRunner.PowerShell(ResolvedScript!, args, exe ?? (Interpreter!.Equals("powershell", StringComparison.OrdinalIgnoreCase) ? "powershell" : null));
                break;
            case "python":
                runner = ProcessRunner.Python(ResolvedScript!, args, exe);
                break;
            default:
                runner = new ProcessRunner(Executable);
                foreach (string a in args) runner.Arg(a);
                if (Encoding.Equals("utf8", StringComparison.OrdinalIgnoreCase)) runner.Utf8();
                break;
        }
        foreach (var (name, value) in Env ?? new()) if (!string.IsNullOrWhiteSpace(name)) runner.Env(name, value ?? "");
        if (WorkingDirectory is { } wd && Resolve(wd, values) is { Length: > 0 } dir) runner.WorkingDirectory(dir);
        if (TimeoutSeconds is double seconds && seconds > 0) runner.Timeout(TimeSpan.FromSeconds(seconds));
        runner.ExitCodes(ApplyExitCodes);
        return runner;
    }
}

/// <summary>Reads a JSON string, number or boolean into a string (so <c>"default": 4</c> and <c>true</c> work).</summary>
internal sealed class ScalarAsStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.TryGetDecimal(out decimal d) ? d.ToString(CultureInfo.InvariantCulture) : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        JsonTokenType.Null => null,
        _ => throw new JsonException($"expected a string, number or true/false, got {reader.TokenType}"),
    };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value);
    }
}

/// <summary>The <c>"theme"</c> of a <see cref="ToolSpec"/>.</summary>
public sealed class ThemeSpec
{
    /// <summary>Accent colour, "#RRGGBB".</summary>
    public string Accent { get; set; } = "#0078D4";
    /// <summary>The dark variant of the theme.</summary>
    public bool Dark { get; set; }
    /// <summary>How strongly backgrounds take the accent's hue, 0–1.</summary>
    public double Tint { get; set; } = 0.15;
}
