using System;

namespace AI_FGF;

/// <summary>How buttons are drawn — a framework option (<see cref="AiFgfEffects"/>).</summary>
public enum AiFgfButtonStyle
{
    /// <summary>The standard flat buttons (the default).</summary>
    Flat,
    /// <summary>Raised and lit, sunk when pressed (WinForms: Direct3D 12; Avalonia: a soft drop shadow).</summary>
    Raised,
    /// <summary>A halo in the accent colour, brighter on focus and hover (WinForms: Direct3D 12; Avalonia: an accent glow).</summary>
    Glow,
}

/// <summary>
/// Visual effects as one framework option. A form's button style is decided, strongest first, by:
/// <list type="number">
/// <item><c>AI_FGF_BUTTONS=flat|raised|glow</c> in the environment (support, screenshots);</item>
/// <item><see cref="UserButtons"/> — what the user picked in the "Theme…" dialog, saved with the form's settings;</item>
/// <item>the form: <c>ToolForm.ButtonStyle</c>, or <c>"buttonStyle"</c> in tool.json;</item>
/// <item><see cref="DefaultButtons"/> — the application's default (flat unless changed).</item>
/// </list>
/// Open forms follow <see cref="Changed"/> at once. Where an effect cannot be drawn (no Direct3D 12) the
/// buttons are flat — same size, colours and text.
/// </summary>
public static class AiFgfEffects
{
    private static AiFgfButtonStyle? _user;

    /// <summary>The application's default for forms that do not choose (set it once at start-up).</summary>
    public static AiFgfButtonStyle DefaultButtons { get; set; } = AiFgfButtonStyle.Flat;

    /// <summary>The user's choice (null = none: the form decides).</summary>
    public static AiFgfButtonStyle? UserButtons
    {
        get => _user;
        set { if (_user != value) { _user = value; Changed?.Invoke(null, EventArgs.Empty); } }
    }

    /// <summary>Raised when <see cref="UserButtons"/> changes; front ends restyle their open forms.</summary>
    public static event EventHandler? Changed;

    /// <summary>The style a form with <paramref name="formStyle"/> shows now.</summary>
    public static AiFgfButtonStyle Resolve(AiFgfButtonStyle formStyle) => FromEnvironment() ?? _user ?? formStyle;

    /// <summary><c>AI_FGF_BUTTONS</c>, when set to a style name.</summary>
    public static AiFgfButtonStyle? FromEnvironment() => TryParse(Environment.GetEnvironmentVariable("AI_FGF_BUTTONS"), out AiFgfButtonStyle s) ? s : null;

    /// <summary>"flat", "raised" (or "3d"), "glow" — any case.</summary>
    public static bool TryParse(string? text, out AiFgfButtonStyle style)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "flat": style = AiFgfButtonStyle.Flat; return true;
            case "raised" or "3d": style = AiFgfButtonStyle.Raised; return true;
            case "glow": style = AiFgfButtonStyle.Glow; return true;
            default: style = AiFgfButtonStyle.Flat; return false;
        }
    }

    /// <summary>The settings-file name of a style ("flat", "raised", "glow").</summary>
    public static string Name(AiFgfButtonStyle style) => style.ToString().ToLowerInvariant();
}
