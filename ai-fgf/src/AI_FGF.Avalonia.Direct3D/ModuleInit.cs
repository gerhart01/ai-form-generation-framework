using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AI_FGF.Avalonia;

namespace AI_FGF.AvaloniaDirect3D;

/// <summary>
/// Plugs this add-on into <see cref="AiFgfRenderers.Direct3D"/> as soon as it is referenced - no call
/// needed: a project that adds a <c>&lt;ProjectReference&gt;</c> to AI_FGF.Avalonia.Direct3D gets
/// the real Direct3D 12 buttons and field focus glow automatically wherever a <c>ToolForm</c> (or a
/// <c>tool.json</c>) asks for them, with no further wiring.
/// </summary>
internal static class ModuleInit
{
    // CA2255 normally flags a module initializer in library code (surprising side effects just from
    // a reference) - the whole point here: referencing this add-on IS the opt-in, exactly as adding
    // the package is for e.g. ASP.NET Core's own use of this pattern.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register() => AiFgfRenderers.Direct3D = new Direct3DRendererBridge();
#pragma warning restore CA2255
}

internal sealed class Direct3DRendererBridge : IAiFgfD3DRenderer
{
    public bool IsAvailable => AiFgfD3D.IsAvailable;

    public Button CreateButton() => new AiFgfD3DButton();

    public Control WrapField(Control inner, double margin) => new AiFgfD3DFieldGlow(inner, margin);

    public void Refresh(Control root, AiFgfButtonStyle style)
    {
        foreach (AiFgfD3DButton b in root.GetVisualDescendants().OfType<AiFgfD3DButton>())
        {
            b.Look = style;
            b.InvalidateVisual();
        }
        foreach (AiFgfD3DFieldGlow g in root.GetVisualDescendants().OfType<AiFgfD3DFieldGlow>())
            g.InvalidateVisual();
    }
}
