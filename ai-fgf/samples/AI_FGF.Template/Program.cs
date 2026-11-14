using System.Text.Json;
using AI_FGF;

namespace AI_FGF.Template;

/// <summary>
/// Shows the form described by a tool spec: <c>AI_FGF.Template.exe [tool.json]</c>
/// (default: tool.json next to the exe). An invalid spec is reported instead of shown —
/// run tools/AI_FGF.Check for the full report including the layout checks.
/// </summary>
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        AiFgfApp.Configure();
        AiFgfCrash.Install();   // unexpected errors become a report file + dialog, not a silent exit
        string path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "tool.json");

        ToolSpec spec;
        try { spec = ToolSpec.FromFile(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show($"Cannot read {path}:\n{ex.Message}", "AI_FGF template", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        var errors = spec.Validate().Where(i => i.IsError).ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show($"{path} has errors:\n\n" + string.Join("\n", errors), "AI_FGF template", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        Application.Run(new SpecToolForm(spec));
        return 0;
    }
}
