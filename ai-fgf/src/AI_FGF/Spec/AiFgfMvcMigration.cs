using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AI_FGF;

/// <summary>One event the old Designer file wired: <c>this.btnStart.Click += new EventHandler(this.Service_Click);</c>.</summary>
/// <param name="Target">The control or component (<c>btnStart</c>), or <c>View</c> for the form itself.</param>
/// <param name="Event">The event (<c>Click</c>).</param>
/// <param name="Handler">The method (<c>Service_Click</c>).</param>
public sealed record AiFgfWiredEvent(string Target, string Event, string Handler);

/// <summary>What <see cref="AiFgfMvcMigration.Split"/> made of an old form: the files to add, and what is left to do by hand.</summary>
public sealed class AiFgfMvcSplit
{
    /// <summary>The old form's class (<c>Form1</c>).</summary>
    public string FormName { get; init; } = "";
    /// <summary>The view's class (<c>Form1View</c>).</summary>
    public string ViewName { get; init; } = "";
    /// <summary>The controller's class (<c>Form1Controller</c>).</summary>
    public string ControllerName { get; init; } = "";
    /// <summary>File name → content: <c>Form1View.cs</c>, <c>Form1View.Designer.cs</c>, <c>Form1Controller.cs</c>, <c>Form1Controller.Controls.cs</c>.</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();
    /// <summary>Every event the Designer wired, now an <see cref="AiFgfOnAttribute"/> on its handler.</summary>
    public IReadOnlyList<AiFgfWiredEvent> Events { get; init; } = Array.Empty<AiFgfWiredEvent>();
    /// <summary>The controls and components of the markup (name → type), each forwarded to the controller.</summary>
    public IReadOnlyList<(string Name, string Type)> Controls { get; init; } = Array.Empty<(string, string)>();
    /// <summary>What the split could not do itself.</summary>
    public IReadOnlyList<string> ToDo { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Splits an old WinForms form into model–view–controller (<see cref="AiFgfMvc"/>) without rewriting its code. The
/// Designer file becomes the <b>view</b>: the same markup with the event wiring taken out, and the fields made
/// <c>internal</c>. The code-behind becomes the <b>controller</b>: every member moves as written, the handlers get
/// <see cref="AiFgfOnAttribute"/>s from the wiring, the form's <c>OnLoad</c>-style overrides become handlers of the
/// view's events, and a partial file gives each control a property of its old name, so <c>txtPath.Text</c> still
/// compiles. <c>AI_FGF.Check mvc Form1.cs</c> writes the files.
/// </summary>
public static class AiFgfMvcMigration
{
    private static readonly Regex ClassName = new(@"\bpartial\s+class\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex Field = new(
        @"^(?<indent>[ \t]*)(?<access>private|protected|internal|public)\s+(?<type>(?:global::)?[\w\.]+(?:<[^>;=]+>)?)\s+(?<name>\w+)\s*(?:=\s*null\s*)?;[ \t]*(?<cr>\r?)$",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Wiring = new(
        @"^[ \t]*this\.(?:(?<target>\w+)\.)?(?<event>\w+)\s*\+=\s*(?:new\s+(?:global::)?[\w\.]+\s*\(\s*)?(?:this\.)?(?<handler>\w+)\s*\)?\s*;[ \t]*\r?\n",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Override = new(
        @"^(?<indent>[ \t]*)protected\s+override\s+void\s+On(?<event>\w+)\s*\(\s*(?<type>(?:global::)?[\w\.]+)\s+(?<arg>\w+)\s*\)",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex OtherOverride = new(@"^[ \t]*(?:protected|public)\s+override\s+[\w\.<>]+\s+(?<name>\w+)\b", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Split the form whose code-behind is <paramref name="formSource"/> and whose Designer file is
    /// <paramref name="designerSource"/>. Nothing is compiled, so the result is only as good as the files are
    /// Designer-shaped; <see cref="AiFgfMvcSplit.ToDo"/> names what it could not move.
    /// </summary>
    public static AiFgfMvcSplit Split(string formSource, string designerSource)
    {
        ArgumentNullException.ThrowIfNull(formSource);
        ArgumentNullException.ThrowIfNull(designerSource);
        string nl = designerSource.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var todo = new List<string>();

        string form = ClassName.Match(designerSource) is { Success: true } m ? m.Groups[1].Value
            : ClassName.Match(formSource) is { Success: true } m2 ? m2.Groups[1].Value
            : throw new ArgumentException("No 'partial class' in the Designer file.", nameof(designerSource));
        string view = form + "View", controller = form + "Controller";
        string ns = Namespace(designerSource) ?? Namespace(formSource) ?? "";

        // ---- the view: the markup, without the wiring, its fields internal
        var events = new List<AiFgfWiredEvent>();
        string markup = Wiring.Replace(designerSource, w =>
        {
            string target = w.Groups["target"].Success ? w.Groups["target"].Value : "View";
            events.Add(new AiFgfWiredEvent(target, w.Groups["event"].Value, w.Groups["handler"].Value));
            return "";
        });
        var controls = new List<(string Name, string Type)>();
        markup = Field.Replace(markup, f =>
        {
            string name = f.Groups["name"].Value;
            if (name == "components") return f.Value;
            controls.Add((name, f.Groups["type"].Value));
            return $"{f.Groups["indent"].Value}internal {f.Groups["type"].Value} {name};{f.Groups["cr"].Value}";
        });
        markup = Regex.Replace(markup, $@"\bpartial\s+class\s+{form}\b", $"partial class {view}");
        markup = Regex.Replace(markup, $@"\btypeof\(\s*{form}\s*\)", $"typeof({view})");
        if (markup.Contains("ComponentResourceManager", StringComparison.Ordinal))
            todo.Add($"rename {form}.resx to {view}.resx (the markup reads its resources as {view})");

        // ---- the controller: the code-behind, every member as written
        int classAt = Regex.Match(formSource, $@"\bclass\s+{form}\b").Index;
        Match header = Regex.Match(formSource[classAt..], $@"class\s+{form}\s*(?::\s*(?<base>[\w\.]+(?:<[^>]+>)?)(?:\s*,\s*[\w\.]+)*)?");
        if (!header.Success) throw new ArgumentException($"No class {form} in the code-behind.", nameof(formSource));
        string baseType = header.Groups["base"].Success ? header.Groups["base"].Value : "Form";
        string code = formSource[..classAt] + $"class {controller} : AiFgfController<{view}>" + formSource[(classAt + header.Length)..];
        code = Regex.Replace(code, $@"\b((?:public|internal|private|protected)\s+){form}\s*\(", $"$1{controller}(");
        code = Regex.Replace(code, $@"\bnew\s+{form}\s*\(", $"new {controller}(");
        code = Regex.Replace(code, @"^[ \t]*(?:this\.)?InitializeComponent\s*\(\s*\)\s*;[ \t]*\r?\n", "", RegexOptions.Multiline);
        code = Regex.Replace(code, $@"(?:\b(?:public|internal|private|sealed|partial)\s+)*class\s+{controller}\b", $"public sealed partial class {controller}");

        // the form's own overrides (OnLoad, OnShown, …) become handlers of the view's events
        code = Override.Replace(code, o =>
        {
            string evt = o.Groups["event"].Value, indent = o.Groups["indent"].Value;
            return $"{indent}[AiFgfOn(\"View\", \"{evt}\")]{nl}{indent}private void View_{evt}(object sender, {o.Groups["type"].Value} {o.Groups["arg"].Value})";
        });
        code = Regex.Replace(code, @"^[ \t]*base\.On\w+\s*\(\s*\w+\s*\)\s*;[ \t]*\r?\n", "", RegexOptions.Multiline);
        foreach (Match o in OtherOverride.Matches(code))
            todo.Add($"{o.Groups["name"].Value}: an override of the form — move it to {view}.cs (the controller is not a form)");

        // the wiring, as attributes on the handlers
        foreach (var group in events.GroupBy(e => e.Handler))
        {
            Match decl = Regex.Match(code, $@"^(?<indent>[ \t]*)(?:(?:private|protected|internal|public|async|static|virtual)\s+)*void\s+{Regex.Escape(group.Key)}\s*\(", RegexOptions.Multiline);
            if (!decl.Success)
            {
                todo.Add($"{group.Key}: wired to {string.Join(", ", group.Select(e => e.Target + "." + e.Event))}, but not found in the code-behind — add it to {controller} with [AiFgfOn(…)]");
                continue;
            }
            string indent = decl.Groups["indent"].Value;
            string attrs = string.Concat(group.Select(e => $"{indent}[AiFgfOn(\"{e.Target}\", \"{e.Event}\")]{nl}"));
            code = code[..decl.Index] + attrs + code[decl.Index..];
        }
        if (!Regex.IsMatch(code, @"^\s*using\s+AI_FGF\s*;", RegexOptions.Multiline))
        {
            Match last = Regex.Matches(code, @"^\s*using\s+[\w\.]+\s*;[ \t]*\r?\n", RegexOptions.Multiline).LastOrDefault() ?? Match.Empty;
            int at = last.Success ? last.Index + last.Length : 0;
            code = code[..at] + "using AI_FGF;" + nl + code[at..];
        }

        // ---- each control as a property of its old name
        var forward = new StringBuilder();
        forward.Append("// Generated by AI_FGF.Check mvc: the view's controls under their old names, so the handlers moved" + nl);
        forward.Append("// from the form compile unchanged. Regenerate after adding a control to the markup." + nl);
        if (ns.Length > 0) forward.Append($"namespace {ns};{nl}{nl}");
        forward.Append($"public sealed partial class {controller}{nl}{{{nl}");
        foreach (var (name, type) in controls) forward.Append($"    private {type} {name} => View.{name};{nl}");
        forward.Append($"}}{nl}");

        var viewCode = new StringBuilder();
        viewCode.Append($"// The view: the markup of {form} ({view}.Designer.cs). Its handlers live in {controller}.{nl}");
        viewCode.Append($"using System.Windows.Forms;{nl}{nl}");
        if (ns.Length > 0) viewCode.Append($"namespace {ns};{nl}{nl}");
        viewCode.Append($"public partial class {view} : {baseType}{nl}{{{nl}    public {view}() => InitializeComponent();{nl}}}{nl}");

        todo.Add($"start it with  Application.Run(new {controller}().View);  (was new {form}())");
        todo.Add($"delete {form}.cs and {form}.Designer.cs once the new files build");
        return new AiFgfMvcSplit
        {
            FormName = form, ViewName = view, ControllerName = controller,
            Files = new Dictionary<string, string>
            {
                [view + ".cs"] = viewCode.ToString(),
                [view + ".Designer.cs"] = markup,
                [controller + ".cs"] = code,
                [controller + ".Controls.cs"] = forward.ToString(),
            },
            Events = events, Controls = controls, ToDo = todo,
        };
    }

    private static string? Namespace(string source)
        => Regex.Match(source, @"^\s*namespace\s+([\w\.]+)", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;
}
