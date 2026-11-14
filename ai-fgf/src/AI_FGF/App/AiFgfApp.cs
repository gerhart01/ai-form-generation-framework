using System;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// One-call application bootstrap. Replaces the identical <c>Main</c> body in every
/// reference project (<c>ApplicationConfiguration.Initialize(); Application.Run(...)</c>),
/// and optionally sets the theme first.
///
/// In your executable:
/// <code>
/// [STAThread]
/// static void Main() => AiFgfApp.Run(() => new MyForm(), AiFgfTheme.Light);
/// </code>
///
/// Remember the executable's csproj still needs the WinForms + high-DPI properties
/// (see the AI_FGF README) — those are MSBuild settings, not runtime calls.
/// </summary>
public static class AiFgfApp
{
    /// <summary>
    /// Configure the app, install crash handling (<see cref="AiFgfCrash"/>; pass options to change
    /// where reports go or what happens on a crash) and run the main form.
    /// </summary>
    public static void Run(Func<Form> createMainForm, AiFgfTheme? theme = null, AiFgfCrashOptions? crash = null)
    {
        if (theme is not null) AiFgfTheme.Current = theme;
        Configure();
        AiFgfCrash.Install(crash);
        using var form = createMainForm();
        Application.Run(form);
    }

    /// <summary>
    /// The runtime setup that <c>ApplicationConfiguration.Initialize()</c> emits in an
    /// executable. Done explicitly here so it also works when called from this library.
    /// </summary>
    public static void Configure()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetDefaultFont(AiFgfTheme.Current.UiFont());
    }
}
