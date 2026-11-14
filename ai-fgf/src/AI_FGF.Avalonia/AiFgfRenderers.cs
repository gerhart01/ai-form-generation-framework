using Avalonia.Controls;

namespace AI_FGF.Avalonia;

/// <summary>
/// Optional hook a Direct3D-capable add-on (<c>AI_FGF.Avalonia.Direct3D</c>, Windows-only,
/// experimental) plugs itself into when its assembly loads, so a <see cref="ToolForm"/> that asks
/// for it (<see cref="ToolForm.UseDirect3D"/>, or a <c>tool.json</c>'s <c>"renderer": "direct3d"</c>)
/// renders the real Direct3D 12 buttons and field focus glow AI_FGF has on WinForms, instead
/// of the cross-platform drop-shadow look (<see cref="AiFgfButtonEffects"/>).
/// <para>
/// AI_FGF.Avalonia itself never references Vortice or touches a GPU: a form that asks for Direct3D
/// without the add-on referenced, or on a machine where it could not initialise (no adapter, a VM with
/// no GPU, Linux/macOS), falls back to the drop-shadow look unchanged - see <see cref="IAiFgfD3DRenderer.IsAvailable"/>.
/// </para>
/// </summary>
public interface IAiFgfD3DRenderer
{
    /// <summary>False if Direct3D 12 could not be initialised (no adapter, a VM with no GPU, not Windows).</summary>
    bool IsAvailable { get; }

    /// <summary>A button that renders itself with Direct3D; its look is set by the next <see cref="Refresh"/>.</summary>
    Button CreateButton();

    /// <summary>Wraps a text/combo/number field with a Direct3D focus glow, in place of the CSS drop shadow.</summary>
    Control WrapField(Control inner, double margin);

    /// <summary>Re-applies <paramref name="style"/> and the current theme to every Direct3D control under <paramref name="root"/>.</summary>
    void Refresh(Control root, AiFgfButtonStyle style);
}

/// <summary>Where <see cref="IAiFgfD3DRenderer"/> is published and looked up.</summary>
public static class AiFgfRenderers
{
    /// <summary>Set by AI_FGF.Avalonia.Direct3D's module initializer as soon as that assembly is referenced.</summary>
    public static IAiFgfD3DRenderer? Direct3D { get; set; }
}
