using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Styling;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Themes.Fluent;

namespace AI_FGF.Avalonia;

/// <summary>
/// The Avalonia application behind AI_FGF windows: Fluent theme dressed in the WinForms look
/// (<see cref="AvTheme"/>), the DataGrid styles, and the Inter font so text renders the same on a
/// Linux box without the usual fonts. Apps start it with <see cref="AiFgfApp.Run"/>.
/// </summary>
public class AiFgfAvaloniaApp : Application
{
    internal static Func<Window>? CreateMain;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://AI_FGF.Avalonia/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"),
        });
        Styles.Add(GridFontStyles());
        Styles.Add(WinFormsStyles());
        HookWinFormsTemplates();
        AvTheme.Apply(this, AiFgfTheme.Current.Palette);
    }

    /// <summary>
    /// WinForms control proportions on the Fluent templates (see <see cref="WinFormsMetrics"/>): check boxes
    /// 20 px tall with the caption after a 13-px glyph, tabs as bordered boxes in the window's font.
    /// </summary>
    private static Styles WinFormsStyles()
    {
        return new Styles
        {
            new Style(x => x.OfType<CheckBox>())
            {
                Setters =
                {
                    new Setter(Layoutable.MinHeightProperty, 0.0),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(WinFormsMetrics.CheckGap, 1.5, WinFormsMetrics.CheckTrail, 1.5)),
                },
            },
            new Style(x => x.OfType<RadioButton>())
            {
                Setters =
                {
                    new Setter(Layoutable.MinHeightProperty, 0.0),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(WinFormsMetrics.CheckGap, 1.5, WinFormsMetrics.CheckTrail, 1.5)),
                },
            },
            new Style(x => x.OfType<TabItem>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1, 1, 1, 0)),
                    new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension(AvTheme.BorderKey)),
                    new Setter(TemplatedControl.FontWeightProperty, FontWeight.Normal),
                },
            },
            new Style(x => x.OfType<TabControl>()) { Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)) } },
            new Style(x => x.OfType<ButtonSpinner>()) { Setters = { new Setter(TemplatedControl.TemplateProperty, SpinnerTemplate()) } },
            new Style(x => x.OfType<NumericUpDown>()) { Setters = { new Setter(TemplatedControl.BorderBrushProperty, new SolidColorBrush(WinFormsMetrics.SpinnerFrame)) } },
        };
    }

    /// <summary>
    /// A NumericUpDown's arrows as WinForms draws them: one narrow column at the right, up over down,
    /// small triangles — not Fluent's two wide side-by-side buttons.
    /// </summary>
    private static FuncControlTemplate<ButtonSpinner> SpinnerTemplate() => new((spinner, scope) =>
    {
        RepeatButton Arrow(string name, string geometry, int row)
        {
            var glyph = AvTheme.Bind(new global::Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(geometry), Stretch = Stretch.None }, Shape.FillProperty, AvTheme.TextKey);
            var b = new RepeatButton
            {
                Name = name, Content = glyph, Focusable = false, IsTabStop = false,
                Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, CornerRadius = new CornerRadius(0),
                BorderThickness = new Thickness(1, row == 0 ? 0 : 0.5, 0, row == 0 ? 0.5 : 0),
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(b, row);
            b.RegisterInNameScope(scope);
            return b;
        }
        var arrows = new Grid
        {
            Width = WinFormsMetrics.SpinWidth, RowDefinitions = new RowDefinitions("*,*"),
            Children = { Arrow("PART_IncreaseButton", "M0,4 L3.5,0 L7,4 Z", 0), Arrow("PART_DecreaseButton", "M0,0 L7,0 L3.5,4 Z", 1) },
            [!Visual.IsVisibleProperty] = spinner[!ButtonSpinner.ShowButtonSpinnerProperty],
        };
        DockPanel.SetDock(arrows, Dock.Right);
        var content = new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            [!ContentPresenter.ContentProperty] = spinner[!ContentControl.ContentProperty],
            [!ContentPresenter.ContentTemplateProperty] = spinner[!ContentControl.ContentTemplateProperty],
            [!ContentPresenter.PaddingProperty] = spinner[!TemplatedControl.PaddingProperty],
        };
        content.RegisterInNameScope(scope);
        return new Border
        {
            [!Border.BackgroundProperty] = spinner[!TemplatedControl.BackgroundProperty],
            [!Border.BorderBrushProperty] = spinner[!TemplatedControl.BorderBrushProperty],
            [!Border.BorderThicknessProperty] = spinner[!TemplatedControl.BorderThicknessProperty],
            Child = new DockPanel { Children = { arrows, content } },
        };
    });

    private static bool _hooked;

    /// <summary>
    /// Sizes fixed inside Fluent templates outrank styles, so they are set when a template is applied:
    /// the check box glyph, the tab strip offset, the selected-tab line on top (as WinForms draws it).
    /// </summary>
    private static void HookWinFormsTemplates()
    {
        if (_hooked) return;
        _hooked = true;
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<CheckBox>((_, e) =>
        {
            if (e.NameScope.Find<Border>("NormalRectangle") is not { } box) return;
            box.Width = box.Height = WinFormsMetrics.CheckGlyph;
            box.CornerRadius = new CornerRadius(2);   // the visual style's rounded glyph
            for (Visual? v = box.GetVisualParent(); v is Layoutable l && v is not CheckBox; v = v.GetVisualParent())
                l.Height = double.NaN;   // the template's 32-px rows: as tall as the caption, like WinForms
            if (box.GetVisualParent() is Grid glyphCell)
            {
                glyphCell.Width = WinFormsMetrics.CheckGlyph;
                glyphCell.VerticalAlignment = VerticalAlignment.Center;   // WinForms CheckAlign.MiddleLeft
                if (glyphCell.GetVisualParent() is Grid root && root.ColumnDefinitions.Count > 0)
                    root.ColumnDefinitions[0].Width = new GridLength(WinFormsMetrics.CheckGlyph);
            }
            foreach (var view in box.GetVisualParent()?.GetVisualChildren().OfType<Viewbox>() ?? Enumerable.Empty<Viewbox>())
                view.Width = view.Height = WinFormsMetrics.CheckGlyph - 3;
        });
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<RadioButton>((_, e) =>
        {
            // the WinForms radio glyph: 13 px, centred on the caption, in a row as tall as the caption (not Fluent's 32)
            if (e.NameScope.Find<global::Avalonia.Controls.Shapes.Ellipse>("OuterEllipse") is not { } outer) return;
            foreach (string name in new[] { "OuterEllipse", "CheckOuterEllipse" })
                if (e.NameScope.Find<global::Avalonia.Controls.Shapes.Ellipse>(name) is { } ring) ring.Width = ring.Height = WinFormsMetrics.CheckGlyph;
            if (e.NameScope.Find<global::Avalonia.Controls.Shapes.Ellipse>("CheckGlyph") is { } dot) dot.Width = dot.Height = 5;
            if (outer.GetVisualParent() is Grid glyphCell)
            {
                glyphCell.Height = double.NaN;
                glyphCell.VerticalAlignment = VerticalAlignment.Center;
                if (glyphCell.GetVisualParent() is Grid root && root.ColumnDefinitions.Count > 0)
                    root.ColumnDefinitions[0].Width = new GridLength(WinFormsMetrics.CheckGlyph);
            }
        });
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TabControl>((_, e) =>
        {
            if (e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter") is { } strip) strip.Margin = WinFormsMetrics.TabStripMargin;
        });
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TabItem>((_, e) =>
        {
            if (e.NameScope.Find<Border>("PART_SelectedPipe") is not { } pipe) return;
            pipe.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            pipe.Margin = new Thickness(0);
        });
    }

    /// <summary>
    /// The DataGrid theme gives cells a fixed 15 px and headers 12 px, whatever the window's font —
    /// cells came out 22 % larger than in the WinForms grid. Like WinForms, cells and headers use the
    /// grid's (the window's) font; headers are semibold.
    /// </summary>
    private static Styles GridFontStyles()
    {
        static Binding GridFontSize() => new(nameof(TemplatedControl.FontSize))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(DataGrid) },
        };
        return new Styles
        {
            new Style(x => x.OfType<DataGridCell>()) { Setters = { new Setter(TemplatedControl.FontSizeProperty, GridFontSize()) } },
            new Style(x => x.OfType<DataGridColumnHeader>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.FontSizeProperty, GridFontSize()),
                    new Setter(TemplatedControl.FontWeightProperty, global::Avalonia.Media.FontWeight.SemiBold),
                },
            },
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && CreateMain is not null)
            desktop.MainWindow = CreateMain();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The app builder: platform detection (X11 on Linux, Win32, macOS), Skia, Inter font.</summary>
    public static AppBuilder Configure() => AppBuilder.Configure<AiFgfAvaloniaApp>().UsePlatformDetect().WithInterFont();
}

/// <summary>
/// Start a AI_FGF app — the same call as the WinForms <c>AiFgfApp.Run</c>:
/// <code>[STAThread] static void Main() => AiFgfApp.Run(() => new MyToolForm(), AiFgfTheme.Light);</code>
/// </summary>
public static class AiFgfApp
{
    public static void Run(Func<Window> createMainForm, AiFgfTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(createMainForm);
        if (theme is not null) AiFgfTheme.Current = theme;
        AiFgfAvaloniaApp.CreateMain = createMainForm;
        AiFgfAvaloniaApp.Configure().StartWithClassicDesktopLifetime(Environment.GetCommandLineArgs().Skip(1).ToArray());
    }
}
