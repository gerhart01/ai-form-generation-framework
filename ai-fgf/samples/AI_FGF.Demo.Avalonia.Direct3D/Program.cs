using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AI_FGF;
using AI_FGF.Avalonia;
using AI_FGF.AvaloniaDirect3D;

namespace AI_FGF.Demo.Avalonia.Direct3D;

/// <summary>
/// Experimental, Windows-only: the same Direct3D 12 button/field rendering AI_FGF has on
/// WinForms (AiFgfD3DButton, AiFgfD3DFieldGlow - see AI_FGF.Avalonia.Direct3D), hand-wired here rather
/// than through tool.json/SpecToolForm, since the declarative row builders do not yet know how to
/// emit these controls. AiFgfD3D.Status prints which adapter (or why it fell back) at the bottom.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AiFgfApp.Run(BuildWindow, AiFgfTheme.MidnightBlue);
    }

    private static Window BuildWindow()
    {
        AiFgfTheme t = AiFgfTheme.Current;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14, Width = 320 };

        panel.Children.Add(new TextBlock { Text = "AI Forms Generation Framework — Direct3D on Avalonia", Foreground = new SolidColorBrush(AvTheme.ToAv(t.Text)), FontWeight = FontWeight.Bold, FontSize = 16, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "The same pixel shader AiFgfButton uses on WinForms, via Vortice.Direct3D12.", Foreground = new SolidColorBrush(AvTheme.ToAv(t.Subtle)), TextWrapping = TextWrapping.Wrap });

        panel.Children.Add(new AiFgfD3DButton { Content = "Save preset...", Look = AiFgfButtonStyle.Glow, Width = 200, Height = 36, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new AiFgfD3DButton { Content = "▶ Run", Look = AiFgfButtonStyle.Glow, Primary = true, Width = 140, Height = 36, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new AiFgfD3DButton { Content = "Raised", Look = AiFgfButtonStyle.Raised, Width = 140, Height = 36, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new AiFgfD3DButton { Content = "Disabled", Look = AiFgfButtonStyle.Glow, Width = 140, Height = 36, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left });

        var glowField = new TextBox { Watermark = "glow field - try focusing it", Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        panel.Children.Add(new AiFgfD3DFieldGlow(glowField) { Width = 300, Height = 32 });

        var sunkenField = new TextBox { Watermark = "sunken field - try focusing it", Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        panel.Children.Add(new AiFgfD3DFieldGlow(sunkenField) { Width = 300, Height = 32, Sunken = true });

        var status = new TextBlock { Foreground = new SolidColorBrush(AvTheme.ToAv(t.Subtle)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(status);

        var window = new Window
        {
            Title = "AI Forms Generation Framework — Direct3D on Avalonia (experimental, Windows-only)",
            Width = 400,
            Height = 480,
            Background = new SolidColorBrush(AvTheme.ToAv(t.App)),
            Content = panel,
        };
        // IsAvailable starts the device, so the status names the adapter rather than "not started"
        window.Opened += (_, _) => { _ = AiFgfD3D.IsAvailable; status.Text = "AiFgfD3D.Status: " + AiFgfD3D.Status; };
        return window;
    }
}
