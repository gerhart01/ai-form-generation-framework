using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AI_FGF;
using AI_FGF.Avalonia;
using Color = System.Drawing.Color;

namespace AI_FGF.AvaloniaDirect3D;

/// <summary>
/// A button rendered by real Direct3D 12 (<see cref="AiFgfD3D"/>) - the Avalonia counterpart of
/// WinForms' <c>AiFgfButton</c>: the same raised bevel / glow halo, the same hover/press easing and
/// pointer specular, the same flat fallback when Direct3D is unavailable. Windows-only; see
/// <see cref="AiFgfD3D"/>'s remarks for why this lives apart from the cross-platform
/// <c>AiFgfButtonEffects</c> drop shadows.
/// </summary>
public sealed class AiFgfD3DButton : Button, IAiFgfD3DPrimary
{
    private float _hoverAnim, _pressAnim;
    private bool _hover, _pressed, _hasFocus;
    private DispatcherTimer? _animTimer;
    private Point? _pointer;

    /// <summary>Raised bevel, or a glow halo.</summary>
    public AiFgfButtonStyle Look { get; set; } = AiFgfButtonStyle.Raised;

    /// <summary>An accent-coloured button (brighter glow, matches <c>AiFgf.Primary</c> on WinForms).</summary>
    public bool Primary { get; set; }

    /// <summary>The glow colour; empty follows the theme's accent (lightened on a primary button).</summary>
    public Color GlowColor { get; set; } = Color.Empty;

    public AiFgfD3DButton()
    {
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Focusable = true;
        // the caption in the middle of the face, as on WinForms: the theme's Button styles (which centre it) do not reach
        // a subclass, so it sat at the top left - above a glowing face's body, which is inset to make room for the halo
        HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
        Padding = new Thickness(10, 4);
        // disabled: the caption in the theme's subtle colour, as on WinForms (it stayed as bright as an enabled one)
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsEnabledProperty) return;
            if (IsEnabled) ClearValue(ForegroundProperty);
            else Foreground = new SolidColorBrush(AvTheme.ToAv(AiFgfTheme.Current.Subtle));
        };
        PointerEntered += (_, _) => { _hover = true; Animate(); };
        PointerExited += (_, _) => { _hover = _pressed = false; _pointer = null; Animate(); };
        PointerMoved += (_, e) => { if (Look == AiFgfButtonStyle.Raised) { _pointer = e.GetPosition(this); InvalidateVisual(); } };
        AddHandler(PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pressed = true; Animate(); } }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => { _pressed = false; Animate(); }, RoutingStrategies.Tunnel);
        GotFocus += (_, _) => { _hasFocus = true; InvalidateVisual(); };
        LostFocus += (_, _) => { _hasFocus = false; InvalidateVisual(); };
    }

    private void Animate()
    {
        _animTimer ??= CreateTimer();
        if (!_animTimer.IsEnabled) _animTimer.Start();
        InvalidateVisual();
    }

    private DispatcherTimer CreateTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        t.Tick += (_, _) => StepAnimation();
        return t;
    }

    private void StepAnimation()
    {
        const float Step = 0.3f;
        float ht = _hover ? 1f : 0f, pt = _pressed ? 1f : 0f;
        _hoverAnim = _hoverAnim < ht ? MathF.Min(ht, _hoverAnim + Step) : MathF.Max(ht, _hoverAnim - Step);
        _pressAnim = _pressAnim < pt ? MathF.Min(pt, _pressAnim + Step) : MathF.Max(pt, _pressAnim - Step);
        InvalidateVisual();
        if (_hoverAnim == ht && _pressAnim == pt) _animTimer!.Stop();
    }

    /// <summary>The colour the face glows in now.</summary>
    public Color EffectiveGlow
    {
        get
        {
            if (!GlowColor.IsEmpty) return GlowColor;
            AiFgfTheme t = AiFgfTheme.Current;
            return Primary ? AiFgfColor.Mix(t.Accent, Color.White, 0.35) : t.Accent;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Look == AiFgfButtonStyle.Flat) { base.Render(context); return; }
        double scale = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        int w = (int)Math.Round(Bounds.Width * scale), h = (int)Math.Round(Bounds.Height * scale);
        if (IsEnabled && w >= 4 && h >= 4)
        {
            AiFgfTheme t = AiFgfTheme.Current;
            Color body = Primary ? t.Accent : t.Surface;
            Color back = t.App;
            bool glowing = Look == AiFgfButtonStyle.Glow;
            Color glow = glowing ? EffectiveGlow : Color.Empty;
            bool specular = Look == AiFgfButtonStyle.Raised && _pointer is { } p;
            var face = new AiFgfFace(w, h, (float)scale, body, back, Color.Transparent, _hoverAnim, _pressAnim,
                glow, glowing && _hasFocus,
                specular ? (float)(_pointer!.Value.X * scale) : -1f, specular ? (float)(_pointer!.Value.Y * scale) : -1f);
            WriteableBitmap? bmp = AiFgfD3D.Draw(face);
            if (bmp is not null) context.DrawImage(bmp, new Rect(Bounds.Size));
        }
        else if (!IsEnabled)
        {
            // disabled: a flat face in the window colour with a border, as on WinForms - it used to be no face at all,
            // just a caption floating among the buttons
            AiFgfTheme t = AiFgfTheme.Current;
            var rect = new Rect(Bounds.Size).Deflate(0.5);
            context.DrawRectangle(new SolidColorBrush(AvTheme.ToAv(t.App)), new Pen(new SolidColorBrush(AvTheme.ToAv(t.Border)), 1), rect, 3, 3);
        }
        base.Render(context);
    }
}
