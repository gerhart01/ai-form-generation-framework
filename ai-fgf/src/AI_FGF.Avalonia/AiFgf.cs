using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AI_FGF.Avalonia;

/// <summary>
/// Control factory with the WinForms <c>AiFgf</c> names and signatures (<see cref="Button"/>,
/// <see cref="SmallButton"/>, <see cref="PrimaryButton"/>, <see cref="Check"/>, <see cref="Text"/>,
/// <see cref="Number"/>, <see cref="Combo"/>, <see cref="Label"/>, the file pickers…), producing
/// Avalonia controls with the same metrics: 96x32 buttons with 12/6 padding, 24-px fields, the UI font.
/// "&amp;" in captions marks the Alt access key, as in WinForms.
/// </summary>
public static class AiFgf
{
    /// <summary>WinForms mnemonic ("&amp;Run", "&amp;&amp;" = literal) → Avalonia access key ("_Run").</summary>
    public static string Mnemonic(string text)
        => text.Replace("&&", "\u0001").Replace("_", "__").Replace('&', '_').Replace("\u0001", "&");

    /// <summary>Captions of buttons and check boxes wrap beyond this many em (they grow in height, not past the screen).</summary>
    public const double MaxCaptionEm = 25;

    /// <summary>A caption that keeps the access key and wraps when its control is capped (see <see cref="MaxCaptionEm"/>).</summary>
    public static AccessText WrappingCaption(string text, TextAlignment alignment = TextAlignment.Center)
        => new() { Text = Mnemonic(text), TextWrapping = TextWrapping.Wrap, TextAlignment = alignment };

    /// <summary>The caption text of a button, check box or label (string or <see cref="AccessText"/> content).</summary>
    public static string Caption(object? content) => content switch
    {
        string s => s,
        TextBlock t => t.Text ?? "",
        _ => "",   // an icon or other control: no caption
    };

    /// <summary>Keep <paramref name="c"/> at most <paramref name="em"/> em wide, following its font size.</summary>
    public static T CapWidth<T>(T c, double em) where T : global::Avalonia.Controls.Primitives.TemplatedControl
    {
        void Apply() => c.MaxWidth = em * c.FontSize * 1.33;
        c.AttachedToVisualTree += (_, _) => Apply();
        c.PropertyChanged += (_, e) => { if (e.Property == global::Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty) Apply(); };
        return c;
    }

    /// <summary>A plain or, when <paramref name="direct3D"/> asks for it and the add-on is available, a Direct3D-rendered button.</summary>
    private static Button NewButton(bool direct3D)
        => direct3D && AiFgfRenderers.Direct3D is { IsAvailable: true } d3 ? d3.CreateButton() : new Button();

    /// <summary>A neutral button: minimum 96x32, grows for long captions (never clipped; wraps past 25 em).</summary>
    public static Button Button(string text, EventHandler? onClick = null, bool direct3D = false)
    {
        Button b = NewButton(direct3D);
        b.Content = WrappingCaption(text);
        b.MinWidth = 96; b.MinHeight = 32;
        b.Padding = WinFormsMetrics.ButtonPadding(12, 6, AvTheme.UiFontSize);
        b.Margin = new Thickness(4, 3);
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        if (onClick is not null) b.Click += (s, e) => onClick(s, e);
        return CapWidth(b, MaxCaptionEm);
    }

    /// <summary>A compact button (Browse…, Next, Clear…).</summary>
    public static Button SmallButton(string text, EventHandler? onClick = null, bool direct3D = false)
    {
        Button b = NewButton(direct3D);
        b.Content = Mnemonic(text);
        b.Padding = WinFormsMetrics.ButtonPadding(8, 4, AvTheme.UiFontSize);
        b.Margin = new Thickness(3);
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        if (onClick is not null) b.Click += (s, e) => onClick(s, e);
        return b;
    }

    /// <summary>Restyle a button as the primary (accent, bold) action, in place.</summary>
    public static Button Primary(Button b)
    {
        if (!b.Classes.Contains("accent")) b.Classes.Add("accent");
        b.FontWeight = FontWeight.Bold;
        if (b is IAiFgfD3DPrimary d3) d3.Primary = true;
        return b;
    }

    public static Button PrimaryButton(string text, EventHandler? onClick = null, bool direct3D = false) => Primary(Button(text, onClick, direct3D));

    public static TextBox Text(string? placeholder = null) => new()
    {
        Watermark = placeholder,
        Margin = WinFormsMetrics.InputMargin,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    public static NumericUpDown Number(decimal min = 0, decimal max = 100, decimal value = 0, int decimals = 0)
        => new AiFgfNumber(decimals) { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max) };

    public static CheckBox Check(string text, bool @checked = false) => CapWidth(new CheckBox
    {
        Content = WrappingCaption(text, TextAlignment.Left),
        IsChecked = @checked,
        Margin = WinFormsMetrics.CheckMargin,
        VerticalAlignment = VerticalAlignment.Center,
    }, MaxCaptionEm);

    public static TextBlock Label(string text, bool subtle = false)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        return subtle ? AvTheme.Bind(t, TextBlock.ForegroundProperty, AvTheme.SubtleKey) : t;
    }

    /// <summary>A drop-down: editable (type or pick) or a fixed list.</summary>
    public static ComboBox Combo(bool editable = true) => new()
    {
        IsEditable = editable,
        Margin = WinFormsMetrics.InputMargin,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    // ------------------------------------------------------------ pickers

    private static TopLevel? Top() => (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
        ?.Windows.FirstOrDefault(w => w.IsActive) ?? (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>"Description|*.ext;*.ext2|…" → picker file types.</summary>
    internal static List<FilePickerFileType>? FileTypes(string? filter)
    {
        var types = new List<FilePickerFileType>();
        string[] parts = (filter ?? "").Split('|');
        for (int i = 0; i + 1 < parts.Length; i += 2)
            types.Add(new FilePickerFileType(parts[i]) { Patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries) });
        return types.Count > 0 ? types : null;
    }

    public static async Task<string?> PickOpenFileAsync(TopLevel? owner, string? filter = null, string? title = null)
    {
        if ((owner ?? Top())?.StorageProvider is not { } sp) return null;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title ?? AiFgfText.SelectFile, AllowMultiple = false, FileTypeFilter = FileTypes(filter ?? AiFgfText.AllFilesFilter) });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> PickSaveFileAsync(TopLevel? owner, string? filter = null, string? title = null, string? defaultName = null)
    {
        if ((owner ?? Top())?.StorageProvider is not { } sp) return null;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title ?? AiFgfText.SaveAs, SuggestedFileName = defaultName, FileTypeChoices = FileTypes(filter ?? AiFgfText.AllFilesFilter) });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> PickFolderAsync(TopLevel? owner, string? description = null)
    {
        if ((owner ?? Top())?.StorageProvider is not { } sp) return null;
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = description ?? AiFgfText.SelectFolder, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>The WinForms-style synchronous picker (runs a nested dispatcher loop until it closes).</summary>
    public static string? PickOpenFile(string? filter = null, string? title = null) => Wait(PickOpenFileAsync(null, filter, title));

    public static string? PickSaveFile(string? filter = null, string? title = null, string? defaultName = null) => Wait(PickSaveFileAsync(null, filter, title, defaultName));

    public static string? PickFolder(string? description = null) => Wait(PickFolderAsync(null, description));

    /// <summary>A "Browse…" button that fills <paramref name="target"/> with a picked file.</summary>
    public static Button BrowseFileInto(TextBox target, string? filter = null, string? title = null, bool direct3D = false)
        => SmallButton(AiFgfText.Browse, async (_, _) => { if (await PickOpenFileAsync(TopLevel.GetTopLevel(target), filter, title) is { } p) target.Text = p; }, direct3D);

    public static Button BrowseFolderInto(TextBox target, string? description = null, bool direct3D = false)
        => SmallButton(AiFgfText.Browse, async (_, _) => { if (await PickFolderAsync(TopLevel.GetTopLevel(target), description) is { } p) target.Text = p; }, direct3D);

    /// <summary>Block (pumping the UI) until <paramref name="task"/> completes — for synchronous, WinForms-style calls.</summary>
    internal static T Wait<T>(Task<T> task)
    {
        if (task.IsCompleted) return task.GetAwaiter().GetResult();
        using var done = new CancellationTokenSource();
        task.ContinueWith(_ => Dispatcher.UIThread.Post(() => done.Cancel()), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(done.Token);
        return task.GetAwaiter().GetResult();
    }
}

/// <summary>
/// Implemented by a Direct3D-rendered button (<c>AiFgfD3DButton</c>, in the optional add-on) so
/// <see cref="AiFgf.Primary"/> can mark it without AI_FGF.Avalonia referencing that project.
/// </summary>
public interface IAiFgfD3DPrimary
{
    bool Primary { get; set; }
}

/// <summary>A multi-line text field that never wraps; it grows from <see cref="VisibleLines"/> to <c>MaxLines</c> lines, then scrolls.</summary>
public class AiFgfMemo : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    public AiFgfMemo()
    {
        AcceptsReturn = true;
        TextWrapping = TextWrapping.NoWrap;
        MinLines = 4;
        MaxLines = 12;
        ScrollViewer.SetHorizontalScrollBarVisibility(this, global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>The fewest lines shown.</summary>
    public int VisibleLines { get => MinLines; set { MinLines = Math.Clamp(value, 1, 100); if (MaxLines < MinLines) MaxLines = MinLines; } }

    /// <summary>WinForms name of <see cref="TextBox.Watermark"/>.</summary>
    public string? PlaceholderText { get => Watermark; set => Watermark = value; }
}

/// <summary>A numeric spinner that formats and reports values with <see cref="DecimalPlaces"/> decimals.</summary>
public class AiFgfNumber : NumericUpDown
{
    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    public AiFgfNumber() : this(0) { }

    public AiFgfNumber(int decimals)
    {
        DecimalPlaces = decimals;
        Margin = WinFormsMetrics.InputMargin;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private int _decimals;

    /// <summary>Decimals shown and returned (WinForms NumericUpDown.DecimalPlaces).</summary>
    public int DecimalPlaces
    {
        get => _decimals;
        set
        {
            _decimals = Math.Clamp(value, 0, 10);
            Increment = _decimals > 0 ? (decimal)Math.Pow(10, -_decimals) : 1;
            FormatString = "F" + _decimals.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
