using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AI_FGF;
using AI_FGF.Avalonia;
using Color = System.Drawing.Color;

namespace AI_FGF.AvaloniaDirect3D;

/// <summary>
/// Wraps a text or combo field with a real Direct3D 12 glow halo (<see cref="AiFgfD3D"/>) that
/// brightens on focus - the Avalonia counterpart of WinForms' <c>AiFgfFieldGlow</c>, including its
/// <see cref="Sunken"/> mode (the field's own Raised face, pressed forced to 1, under a plain
/// a plain DrawingContext-drawn focus ring - the same pragmatic compromise AiFgfFieldGlow uses on WinForms, since a
/// second opaque Direct3D pass would paint over the first rather than blend with it). The field's
/// own text/caret/selection stay ordinary Avalonia controls; only the background/surrounding halo
/// comes from Direct3D. Windows-only - see <see cref="AiFgfD3D"/>.
/// </summary>
public sealed class AiFgfD3DFieldGlow : Decorator
{
    private float _focusAnim;
    private bool _hasFocus;
    private DispatcherTimer? _animTimer;

    /// <summary>The glow colour; empty follows the theme's accent.</summary>
    public Color GlowColor { get; set; } = Color.Empty;

    /// <summary>Also draw the field permanently sunken (AiFgfD3D's Raised face, pressed forced to 1).</summary>
    public bool Sunken { get; set; }

    public AiFgfD3DFieldGlow(Control inner, double margin = 4)
    {
        Padding = new Thickness(margin);
        Child = inner;
        inner.GotFocus += (_, _) => { _hasFocus = true; Animate(); };
        inner.LostFocus += (_, _) => { _hasFocus = false; Animate(); };
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
        const float Step = 0.25f;
        float target = _hasFocus ? 1f : 0f;
        _focusAnim = _focusAnim < target ? MathF.Min(target, _focusAnim + Step) : MathF.Max(target, _focusAnim - Step);
        InvalidateVisual();
        if (_focusAnim == target) _animTimer!.Stop();
    }

    public override void Render(DrawingContext context)
    {
        double scale = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        int w = (int)Math.Round(Bounds.Width * scale), h = (int)Math.Round(Bounds.Height * scale);
        if (w >= 4 && h >= 4)
        {
            AiFgfTheme t = AiFgfTheme.Current;
            Color body = t.Surface;
            Color back = t.App;
            Color glow = GlowColor.IsEmpty ? t.Accent : GlowColor;

            if (Sunken)
            {
                WriteableBitmap? sunk = AiFgfD3D.Draw(new AiFgfFace(w, h, (float)scale, body, back, Color.Transparent, 0f, 1f));
                if (sunk is not null) context.DrawImage(sunk, new Rect(Bounds.Size));
                if (_focusAnim > 0.01f)
                {
                    double width = Math.Max(1.5, 2.5 * scale);
                    var pen = new Pen(new SolidColorBrush(global::Avalonia.Media.Color.FromArgb((byte)(235 * _focusAnim), glow.R, glow.G, glow.B)), width);
                    double inset = width / 2 + 1;
                    context.DrawRectangle(pen, new Rect(inset, inset, Math.Max(0, Bounds.Width - 2 * inset), Math.Max(0, Bounds.Height - 2 * inset)));
                }
            }
            else
            {
                WriteableBitmap? face = AiFgfD3D.Draw(new AiFgfFace(w, h, (float)scale, body, back, Color.Transparent, _focusAnim, 0f, glow, _hasFocus));
                if (face is not null) context.DrawImage(face, new Rect(Bounds.Size));
            }
        }
        base.Render(context);
    }
}
