using System.Globalization;
using System.Text.Json;
using AI_FGF;

namespace AI_FGF.Check;

/// <summary>
/// <c>AI_FGF.Check tool.json [--dpi 96,120,144,192]</c> — is the form this spec describes
/// correct? Validates the spec, shows the command line the defaults produce, builds the form
/// and runs the layout conformance check at each DPI (real WM_DPICHANGED).
/// <c>AI_FGF.Check --schema [file]</c> — writes the JSON Schema of tool.json.
/// <c>AI_FGF.Check draft &lt;assembly&gt; &lt;form type&gt; [-o tool.json]</c> — drafts a spec from an existing form.
/// <c>AI_FGF.Check compare &lt;reference.json&gt; &lt;snapshot.json&gt; [--content]</c> — compares two layout snapshots.
/// <c>AI_FGF.Check mvc &lt;Form1.cs&gt; [Form1.Designer.cs] [-o dir]</c> — splits a form into view (its markup) and controller (its handlers).
/// Exit code: 0 = PASS, 1 = FAIL, 2 = the input could not be read.
/// </summary>
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length > 0 && args[0] == "--schema") return Schema(args.ElementAtOrDefault(1));
        if (args.Length > 0 && args[0] == "draft") return Draft(args[1..]);
        if (args.Length > 0 && args[0] == "compare") return Compare(args[1..]);
        if (args.Length > 0 && args[0] == "mvc") return Mvc(args[1..]);
        string path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "tool.json";
        int[] dpis = ParseDpis(args) ?? new[] { 96, 120, 144, 192 };
        AiFgfApp.Configure();

        ToolSpec spec;
        try { spec = ToolSpec.FromFile(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"FAIL  cannot read {path}: {ex.Message}");
            return 2;
        }

        Console.WriteLine($"Spec      {Path.GetFullPath(path)}");
        Console.WriteLine($"Title     {spec.Title}");
        var issues = spec.Validate();
        foreach (SpecIssue issue in issues) Console.WriteLine($"  {issue}");
        if (issues.Any(i => i.IsError))
        {
            Console.WriteLine($"RESULT: FAIL — {issues.Count(i => i.IsError)} spec error(s); fix them before the layout can be checked.");
            return 1;
        }

        Console.WriteLine($"Command   {Preview(spec)}");

        spec.SettingsKey = "";   // checking must not read or write the user's saved settings
        var layout = AiFgfConformance.CheckAcrossDpi(() => new SpecToolForm(spec), dpis);
        Console.WriteLine($"Layout    checked at {string.Join(", ", dpis)} DPI");
        foreach (ConformanceIssue issue in layout) Console.WriteLine($"  layout: {issue}");

        int warnings = issues.Count(i => !i.IsError);
        bool pass = layout.Count == 0;
        Console.WriteLine(pass
            ? $"RESULT: PASS{(warnings > 0 ? $" ({warnings} warning(s))" : "")}"
            : $"RESULT: FAIL — {layout.Count} layout issue(s)");
        return pass ? 0 : 1;
    }

    private static int Schema(string? path)
    {
        string schema = ToolSpecSchema.Generate();
        if (path is null) Console.WriteLine(schema);
        else { File.WriteAllText(path, schema + Environment.NewLine); Console.WriteLine($"Schema    {Path.GetFullPath(path)}"); }
        return 0;
    }

    /// <summary>
    /// An existing form → a tool.json draft, the old form's snapshot beside it, and a check that the
    /// form the draft makes still has every caption and input of the old one.
    /// </summary>
    private static int Draft(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: AI_FGF.Check draft <assembly.dll|exe> <Namespace.FormType> [-o tool.json]");
            return 2;
        }
        int o = Array.IndexOf(args, "-o");
        string output = o >= 0 && o + 1 < args.Length ? args[o + 1] : "tool.json";
        AiFgfApp.Configure();
        Form old;
        try
        {
            var asm = System.Reflection.Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type type = asm.GetType(args[1], throwOnError: true)!;
            old = (Form)Activator.CreateInstance(type, nonPublic: true)!;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or TypeLoadException or MissingMethodException or InvalidCastException or System.Reflection.TargetInvocationException)
        {
            Console.WriteLine($"FAIL  cannot create {args[1]} from {args[0]}: {ex.GetBaseException().Message}");
            return 2;
        }

        using (old)
        {
            Show(old);
            ToolSpec spec = AiFgfMigration.DraftSpec(old);
            File.WriteAllText(output, spec.ToCompactJson() + Environment.NewLine);
            AiFgfLayoutSnapshot before = AiFgfSnapshot.Of(old);
            string snapshotPath = Path.ChangeExtension(output, ".old-snapshot.json");
            before.Save(snapshotPath);
            Console.WriteLine($"Form      {args[1]} — {spec.Fields.Count} field(s)");
            foreach (FieldSpec f in spec.Fields)
                Console.WriteLine($"  {f.Kind.ToString().ToLowerInvariant(),-7} {f.Key,-20} \"{f.Label}\"{(f.Default is null ? "" : $" = {f.Default}")}");
            Console.WriteLine($"Draft     {Path.GetFullPath(output)}");
            Console.WriteLine($"Snapshot  {Path.GetFullPath(snapshotPath)}");

            foreach (SpecIssue issue in spec.Validate()) Console.WriteLine($"  to do: {issue}");
            spec.SettingsKey = "";   // the check must not read or write the user's saved settings
            using var made = new SpecToolForm(spec);
            Show(made);
            var lost = AiFgfSnapshot.Of(made).Compare(before, AiFgfSnapshotTolerance.ContentOnly);
            foreach (string l in lost) Console.WriteLine($"  not in the new form: {l}");
            Console.WriteLine("Next      set \"executable\" and each field's \"arg\", then: AI_FGF.Check " + output);
            Console.WriteLine(lost.Count == 0 ? "RESULT: PASS — every caption and input carried over" : $"RESULT: FAIL — {lost.Count} element(s) of the old form are not in the new one (add them or confirm they are not needed)");
            return lost.Count == 0 ? 0 : 1;
        }
    }

    private static void Show(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new System.Drawing.Point(-10000, -10000);
        f.ShowInTaskbar = false;
        f.Show();
        Application.DoEvents();
    }

    private static int Compare(string[] args)
    {
        string[] files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (files.Length < 2)
        {
            Console.WriteLine("usage: AI_FGF.Check compare <reference.json> <snapshot.json> [--content]");
            return 2;
        }
        AiFgfLayoutSnapshot reference, candidate;
        try { (reference, candidate) = (AiFgfLayoutSnapshot.Load(files[0]), AiFgfLayoutSnapshot.Load(files[1])); }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Console.WriteLine($"FAIL  cannot read a snapshot: {ex.Message}");
            return 2;
        }
        var issues = candidate.Compare(reference, args.Contains("--content") ? AiFgfSnapshotTolerance.ContentOnly : AiFgfSnapshotTolerance.Default);
        foreach (string i in issues) Console.WriteLine($"  {i}");
        Console.WriteLine(issues.Count == 0 ? "RESULT: PASS" : $"RESULT: FAIL — {issues.Count} difference(s)");
        return issues.Count == 0 ? 0 : 1;
    }

    /// <summary>The command line produced by the fields' default values.</summary>
    private static string Preview(ToolSpec spec)
    {
        var values = spec.Fields.ToDictionary(f => f.Key, f => f.Default ?? (f.Kind == FieldKind.Number ? (f.Min ?? 0).ToString(CultureInfo.InvariantCulture) : ""), StringComparer.OrdinalIgnoreCase);
        return spec.Executable + " " + string.Join(' ', spec.BuildArguments(values).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
    }

    private static int[]? ParseDpis(string[] args)
    {
        int i = Array.IndexOf(args, "--dpi");
        if (i < 0 || i + 1 >= args.Length) return null;
        return args[i + 1].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    }

    /// <summary>
    /// <c>mvc Form1.cs [Form1.Designer.cs] [-o dir]</c>: split the form into Form1View (the Designer markup, without
    /// its wiring) and Form1Controller (the code-behind with every handler, bound by [AiFgfOn]), written next to it or
    /// into dir. The old files are left as they are.
    /// </summary>
    private static int Mvc(string[] args)
    {
        int o = Array.IndexOf(args, "-o");
        string? outDir = o >= 0 && o + 1 < args.Length ? args[o + 1] : null;
        var files = args.Where((a, i) => i != o && i != o + 1).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine("usage: AI_FGF.Check mvc <Form1.cs> [Form1.Designer.cs] [-o dir]");
            return 2;
        }
        string form = files[0];
        string designer = files.Count > 1 ? files[1] : Path.ChangeExtension(form, ".Designer.cs");
        AiFgfMvcSplit split;
        try { split = AiFgfMvcMigration.Split(File.ReadAllText(form), File.ReadAllText(designer)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.WriteLine($"FAIL  cannot split {form}: {ex.Message}");
            return 2;
        }
        outDir ??= Path.GetDirectoryName(Path.GetFullPath(form))!;
        Directory.CreateDirectory(outDir);
        foreach (var (name, text) in split.Files)
        {
            File.WriteAllText(Path.Combine(outDir, name), text);
            Console.WriteLine($"wrote     {Path.Combine(outDir, name)}");
        }
        Console.WriteLine($"View      {split.ViewName}: {split.Controls.Count} control(s) and component(s)");
        Console.WriteLine($"Controller {split.ControllerName}: {split.Events.Count} event(s) bound with [AiFgfOn]");
        foreach (AiFgfWiredEvent e in split.Events) Console.WriteLine($"  {e.Target}.{e.Event} -> {e.Handler}");
        foreach (string t in split.ToDo) Console.WriteLine($"to do     {t}");
        Console.WriteLine("RESULT: PASS");
        return 0;
    }
}
