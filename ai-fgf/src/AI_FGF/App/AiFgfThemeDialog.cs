using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// Lets the user choose the colours: an accent (ready-made swatches or any colour), light or
/// dark, and how strongly the backgrounds are tinted. Every change is applied to all open windows
/// at once (<see cref="AiFgfTheme.Switch(AiFgfTheme)"/>) so it is previewed live; Cancel or Esc puts the previous
/// theme back. The button style (<see cref="AiFgfEffects.UserButtons"/>: flat, raised, glowing) is chosen here too,
/// previewed and reverted the same way. Whatever is chosen stays readable — <see cref="AiFgfTheme.Create"/> keeps every
/// text/background pair at 4.5:1 or better.
/// <code>
/// AiFgfThemeChoice? chosen = AiFgfThemeDialog.Show(this);   // null = cancelled (theme restored)
/// </code>
/// </summary>
public sealed class AiFgfThemeDialog : IDisposable
{
    private readonly AiFgfTheme _original;
    private readonly AiFgfButtonStyle? _originalButtons = AiFgfEffects.UserButtons;
    private readonly ComboBox _buttons;
    private readonly Dictionary<int, Button> _swatches = new();
    private readonly ComboBox _mode;
    private readonly TrackBar _tint;
    private readonly Label _tintValue;

    /// <summary>The dialog (shown by <see cref="Show"/>; built but not shown when created directly).</summary>
    public AiFgfDialog Dialog { get; }

    /// <summary>The choice as it stands (applied live).</summary>
    public AiFgfThemeChoice Choice { get; private set; }

    /// <param name="initial">The colours to start from (default: the current theme's).</param>
    /// <param name="buttons">The button style shown now (default: the application's).</param>
    public AiFgfThemeDialog(AiFgfThemeChoice? initial = null, AiFgfButtonStyle? buttons = null)
    {
        _original = AiFgfTheme.Current;
        Choice = initial ?? _original.Choice ?? new AiFgfThemeChoice(_original.Accent, Dwm.IsDark(_original), 0);

        var swatchBar = new FlowLayoutPanel
        {
            AutoSize = false,          // ParamTable gives it the wrapped height it needs
            WrapContents = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(0, 2, 0, 2),
        };
        AiFgf.CentreRows(swatchBar, logicalSide: 3);   // swatches and Custom… on one line, gaps scaled (a flow panel does not scale margins)
        foreach (var (name, color) in AiFgfTheme.Accents) swatchBar.Controls.Add(Swatch(name, color));
        swatchBar.Controls.Add(AiFgf.SmallButton(AiFgfText.ThemeCustomAccent, (_, _) => PickCustom()));

        _tint = new TrackBar
        {
            Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10,
            TickStyle = TickStyle.None,   // ticks below the thumb pushed it above its label's line; "40 %" beside it says the value
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            BackColor = AiFgfTheme.Current.App,
            Value = (int)Math.Round(Choice.Tint * 100),
            AccessibleName = AiFgfText.ThemeTint.Replace("&", "", StringComparison.Ordinal).TrimEnd(':'),
        };
        AiFgf.FitTrackBarToThumb(_tint);
        _tintValue = new Label { AutoSize = true, ForeColor = AiFgfTheme.Current.Text, Margin = new Padding(6, 6, 3, 3), Text = "100 %" };

        Dialog = AiFgfDialog.Create(AiFgfText.ThemeTitle, rows => rows
            .Custom(AiFgfText.ThemeAccent, swatchBar)
            .Choice(AiFgfText.ThemeAppearance, new[] { AiFgfText.ThemeLight, AiFgfText.ThemeDark }, Choice.Dark ? AiFgfText.ThemeDark : AiFgfText.ThemeLight, key: "Appearance")
            .Custom(AiFgfText.ThemeTint, _tint, _tintValue)
            .Choice(AiFgfText.ThemeButtons, Enum.GetValues<AiFgfButtonStyle>().Select(AiFgfText.ButtonStyleName).ToArray(),
                    AiFgfText.ButtonStyleName(buttons ?? AiFgfEffects.Resolve(AiFgfEffects.DefaultButtons)), key: "Buttons"));
        _mode = (ComboBox)Dialog.Fields["Appearance"];
        _buttons = (ComboBox)Dialog.Fields["Buttons"];
        _buttons.SelectedIndexChanged += (_, _) => AiFgfEffects.UserButtons = (AiFgfButtonStyle)_buttons.SelectedIndex;   // live in every window
        _tintValue.Text = $"{_tint.Value} %";

        _mode.SelectedIndexChanged += (_, _) => FromControls();
        _tint.ValueChanged += (_, _) => FromControls();
        Dialog.FormClosed += (_, _) => { if (Dialog.DialogResult != DialogResult.OK) Revert(); };
        Highlight();
    }

    /// <summary>Show modally; the chosen colours, or null if cancelled (the previous theme is restored).</summary>
    public static AiFgfThemeChoice? Show(IWin32Window? owner = null, AiFgfThemeChoice? initial = null)
    {
        using var d = new AiFgfThemeDialog(initial, (owner as ToolForm)?.EffectiveButtonStyle);
        return d.Dialog.ShowDialog(owner) == DialogResult.OK ? d.Choice : null;
    }

    public void SelectAccent(Color accent) => Apply(Choice with { Accent = accent });

    public void SetDark(bool dark) => _mode.SelectedIndex = dark ? 1 : 0;

    /// <summary>Pick a button style (as the user would).</summary>
    public void SetButtons(AiFgfButtonStyle style) => _buttons.SelectedIndex = (int)style;

    public void SetTint(double tint) => _tint.Value = (int)Math.Round(Math.Clamp(tint, 0, 1) * 100);

    /// <summary>The swatch button of a ready-made accent (for tests and automation).</summary>
    public Button SwatchFor(Color accent) => _swatches[accent.ToArgb()];

    private Button Swatch(string name, Color color)
    {
        var b = new AiFgfButton
        {
            Text = "",
            AutoSize = false,
            Size = new Size(28, 28),
            Margin = new Padding(2, 3, 2, 3),
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            Cursor = Cursors.Hand,
            AccessibleName = AiFgfText.AccentName(name),
            Tag = AiFgfTheme.KeepColors,   // a swatch keeps its colour when the theme changes
        };
        b.FlatAppearance.MouseOverBackColor = color;
        b.FlatAppearance.MouseDownBackColor = color;
        b.Click += (_, _) => SelectAccent(color);
        new ToolTip().SetToolTip(b, AiFgfText.AccentName(name));
        _swatches[color.ToArgb()] = b;
        return b;
    }

    private void PickCustom()
    {
        using var cd = new ColorDialog { Color = Choice.Accent, FullOpen = true, AnyColor = true };
        if (cd.ShowDialog(Dialog) == DialogResult.OK) SelectAccent(cd.Color);
    }

    private void FromControls()
    {
        Apply(new AiFgfThemeChoice(Choice.Accent, _mode.SelectedIndex == 1, _tint.Value / 100.0));
    }

    private void Apply(AiFgfThemeChoice choice)
    {
        Choice = choice;
        _tintValue.Text = $"{_tint.Value} %";
        Highlight();
        AiFgfTheme.Switch(choice.ToTheme());   // live preview in every open window, this one included
    }

    /// <summary>The selected swatch gets a thick border in the text colour; the others a thin one.</summary>
    private void Highlight()
    {
        foreach (var (argb, b) in _swatches)
        {
            bool selected = argb == Choice.Accent.ToArgb();
            b.FlatAppearance.BorderSize = selected ? 3 : 1;
            b.FlatAppearance.BorderColor = selected ? AiFgfTheme.Current.Text : AiFgfTheme.Current.Border;
            b.AccessibleDescription = selected ? AiFgfText.ThemeSelected : null;
        }
    }

    private void Revert()
    {
        if (!ReferenceEquals(AiFgfTheme.Current, _original)) AiFgfTheme.Switch(_original);
        AiFgfEffects.UserButtons = _originalButtons;
    }

    public void Dispose()
    {
        if (Dialog.DialogResult != DialogResult.OK) Revert();
        Dialog.Dispose();
    }
}
