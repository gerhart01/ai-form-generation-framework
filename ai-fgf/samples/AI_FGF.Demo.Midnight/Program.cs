using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using AI_FGF;
using AI_FGF.Avalonia;

namespace AI_FGF.Demo.Midnight;

/// <summary>
/// Showcases AI_FGF's Avalonia front end with a dark, MidnightBlue-style theme and Glow buttons
/// and fields (the Avalonia counterpart of AiFgfFieldGlow + AiFgfTheme.MidnightBlue on WinForms) - no code,
/// just tool.json's own "theme" and "buttonStyle" (see AiFgfButtonEffects.For, the Glow branch).
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
