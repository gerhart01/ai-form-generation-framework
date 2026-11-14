using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using DColor = System.Drawing.Color;

namespace AI_FGF.Avalonia;

/// <summary>
/// The colour chooser — the Avalonia twin of the WinForms <c>AiFgfThemeDialog</c>: ready-made accent
/// swatches (and a custom hex colour), Light/Dark, background tint; every change applies to all
/// windows at once, Cancel/Esc restores the previous theme; the button style (<see cref="AiFgfEffects.UserButtons"/>) likewise. Any choice stays readable
/// (<see cref="AiFgfPalette"/> keeps text at 4.5:1).
/// </summary>
public sealed class AiFgfThemeDialog : IDisposable
{
    private readonly AiFgfTheme _original;
    private readonly AiFgfButtonStyle? _originalButtons = AiFgfEffects.UserButtons;
    private readonly ComboBox _buttons;
    private readonly Dictionary<int, Button> _swatches = new();
    private readonly ComboBox _mode;
    private readonly Slider _tint;
    private readonly TextBlock _tintValue;
    private readonly TextBox _hex;

    public AiFgfDialog Dialog { get; }

    public AiFgfThemeChoice Choice { get; private set; }

    /// <param name="initial">The colours to start from (default: the current theme's).</param>
    /// <param name="buttons">The button style shown now (default: the application's).</param>
    public AiFgfThemeDialog(AiFgfThemeChoice? initial = null, AiFgfButtonStyle? buttons = null)
    {
        _original = AiFgfTheme.Current;
        Choice = initial ?? _original.Choice;

        var swatchBar = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, color) in AiFgfTheme.Accents) swatchBar.Children.Add(Swatch(name, color));
        _hex = new TextBox { Width = 90, Text = AiFgfColor.ToHex(Choice.Accent), Margin = new Thickness(6, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center };
        _hex.LostFocus += (_, _) => { if (AiFgfColor.TryParseHex(_hex.Text, out DColor c)) SelectAccent(c); };
        swatchBar.Children.Add(_hex);

        _tint = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = false, Value = Math.Round(Choice.Tint * 100), MinWidth = 160 };
        _tintValue = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 44 };

        Dialog = AiFgfDialog.Create(AiFgfText.ThemeTitle, rows => rows
            .Custom(AiFgfText.ThemeAccent, swatchBar)
            .Choice(AiFgfText.ThemeAppearance, new[] { AiFgfText.ThemeLight, AiFgfText.ThemeDark }, Choice.Dark ? AiFgfText.ThemeDark : AiFgfText.ThemeLight, key: "Appearance")
            .Custom(AiFgfText.ThemeTint, _tint, _tintValue)
            .Choice(AiFgfText.ThemeButtons, Enum.GetValues<AiFgfButtonStyle>().Select(AiFgfText.ButtonStyleName).ToArray(),
                    AiFgfText.ButtonStyleName(buttons ?? AiFgfEffects.Resolve(AiFgfEffects.DefaultButtons)), key: "Buttons"));
        _mode = (ComboBox)Dialog.Fields["Appearance"];
        _buttons = (ComboBox)Dialog.Fields["Buttons"];
        _buttons.SelectionChanged += (_, _) => { if (_buttons.SelectedIndex >= 0) AiFgfEffects.UserButtons = (AiFgfButtonStyle)_buttons.SelectedIndex; };
        _tintValue.Text = $"{(int)_tint.Value} %";

        _mode.SelectionChanged += (_, _) => FromControls();
        _tint.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) FromControls(); };
        Dialog.Closed += (_, _) => { if (!Dialog.Accepted) Revert(); };
        Highlight();
    }

    /// <summary>Modal, synchronous: the chosen colours, or null if cancelled (the previous theme is restored).</summary>
    public static AiFgfThemeChoice? Show(Window? owner = null, AiFgfThemeChoice? initial = null) => AiFgf.Wait(ShowAsync(owner, initial));

    public static async Task<AiFgfThemeChoice?> ShowAsync(Window? owner = null, AiFgfThemeChoice? initial = null)
    {
        var d = new AiFgfThemeDialog(initial, (owner as ToolForm)?.EffectiveButtonStyle);
        if (owner is not null) await d.Dialog.ShowDialog(owner);
        else
        {
            var closed = new TaskCompletionSource();
            d.Dialog.Closed += (_, _) => closed.TrySetResult();
            d.Dialog.Show();
            await closed.Task;
        }
        return d.Dialog.Accepted ? d.Choice : null;
    }

    public void SelectAccent(DColor accent) => Apply(Choice with { Accent = accent });

    public void SetDark(bool dark) => _mode.SelectedIndex = dark ? 1 : 0;

    /// <summary>Pick a button style (as the user would).</summary>
    public void SetButtons(AiFgfButtonStyle style) => _buttons.SelectedIndex = (int)style;

    public void SetTint(double tint) => _tint.Value = Math.Round(Math.Clamp(tint, 0, 1) * 100);

    public Button SwatchFor(DColor accent) => _swatches[accent.ToArgb()];

    private Button Swatch(string name, DColor color)
    {
        var b = new Button
        {
            Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Margin = new Thickness(2, 3),
            Background = AvTheme.Brush(color), Padding = new Thickness(0),
        };
        ToolTip.SetTip(b, AiFgfText.AccentName(name));
        AutomationName(b, AiFgfText.AccentName(name));
        b.Click += (_, _) => SelectAccent(color);
        // the swatch keeps its colour on hover/press (Fluent would repaint it from resources)
        b.Resources["ButtonBackgroundPointerOver"] = AvTheme.Brush(color);
        b.Resources["ButtonBackgroundPressed"] = AvTheme.Brush(color);
        _swatches[color.ToArgb()] = b;
        return b;
    }

    private static void AutomationName(Control c, string name) => global::Avalonia.Automation.AutomationProperties.SetName(c, name);

    private void FromControls() => Apply(new AiFgfThemeChoice(Choice.Accent, _mode.SelectedIndex == 1, _tint.Value / 100.0));

    private void Apply(AiFgfThemeChoice choice)
    {
        Choice = choice;
        _tintValue.Text = $"{(int)_tint.Value} %";
        _hex.Text = AiFgfColor.ToHex(choice.Accent);
        Highlight();
        AiFgfTheme.Switch(choice.ToTheme());   // live preview in every window
    }

    private void Highlight()
    {
        foreach (var (argb, b) in _swatches)
        {
            bool selected = argb == Choice.Accent.ToArgb();
            b.BorderThickness = new Thickness(selected ? 3 : 1);
            b.BorderBrush = AvTheme.Brush(selected ? AiFgfTheme.Current.Text : AiFgfTheme.Current.Border);
        }
    }

    private void Revert()
    {
        if (!ReferenceEquals(AiFgfTheme.Current, _original)) AiFgfTheme.Switch(_original);
        AiFgfEffects.UserButtons = _originalButtons;
    }

    public void Dispose()
    {
        if (!Dialog.Accepted) Revert();
        Dialog.Close();
    }
}
