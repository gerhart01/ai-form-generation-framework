using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using AI_FGF;
using AI_FGF.Avalonia;

namespace AI_FGF.Demo.Direct3D.Spec;

/// <summary>
/// Showcases the declarative path to the real Direct3D 12 renderer: the same tool.json/SpecToolForm
/// pipeline as every other sample, with one extra field ("renderer": "direct3d") - no AiFgfD3DButton,
/// no AiFgfD3DFieldGlow and no AI_FGF.Avalonia.Direct3D type anywhere in this file. Referencing the
/// add-on project is the entire opt-in (see ModuleInit in AI_FGF.Avalonia.Direct3D); ToolForm
/// picks it up through SpecToolForm.UseDirect3D. Windows-only.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "tool.json");
        ToolSpec? spec = null;
        string? problem = null;
        try
        {
            spec = ToolSpec.FromFile(path);
            var errors = spec.Validate().Where(i => i.IsError).ToList();
            if (errors.Count > 0) problem = $"{path}:\n" + string.Join("\n", errors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            problem = $"Cannot read {path}:\n{ex.Message}";
        }
        if (problem is not null) Console.Error.WriteLine(problem);
        AiFgfApp.Run(() => problem is null ? new SpecToolForm(spec!) : ErrorWindow(problem));
        return problem is null ? 0 : 2;
    }

    private static Window ErrorWindow(string message) => new()
    {
        Title = "AI_FGF - spec error",
        Width = 640, Height = 240,
        Content = new TextBlock { Text = message, Margin = new global::Avalonia.Thickness(16), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top },
    };
}
