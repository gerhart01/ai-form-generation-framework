using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AI_FGF;

/// <summary>
/// Direct3D 12 faces for the WinForms controls (<see cref="AiFgfButton.Look"/>, field glows, tabs, progress bars, spin
/// buttons): <see cref="AiFgfFaceRenderer"/> draws the face — a rounded, top-lit body with a bevel and a soft shadow,
/// raised, lighter on hover, sunk when pressed; or a glowing one in a soft halo — and this turns its pixels into a GDI
/// bitmap the control paints (text stays GDI: crisp, with access keys). Bitmaps are cached per size, colours and state,
/// so the GPU works only when a control changes. Without Direct3D 12 the controls paint flat (<see cref="Status"/> says why).
/// </summary>
public static class AiFgfDirect3D
{
    static AiFgfDirect3D() => AiFgfFaceRenderer.Reset += Clear;

    /// <inheritdoc cref="AiFgfFaceRenderer.Status"/>
    public static string Status => AiFgfFaceRenderer.Status;

    /// <inheritdoc cref="AiFgfFaceRenderer.IsAvailable"/>
    public static bool IsAvailable => AiFgfFaceRenderer.IsAvailable;

    /// <inheritdoc cref="AiFgfFaceRenderer.Renders"/>
    public static int Renders => AiFgfFaceRenderer.Renders;

    /// <summary>Turn the renderer off (controls go flat); a later <see cref="Restart"/> may bring it back.</summary>
    public static void Disable(string reason) => AiFgfFaceRenderer.Disable(reason);

    /// <summary>Forget a failure and try to start again on next use.</summary>
    public static void Restart() => AiFgfFaceRenderer.Restart();

    private static readonly Dictionary<AiFgfFace, Bitmap> Cache = new();
    private static readonly Queue<AiFgfFace> CacheOrder = new();
    private const int CacheLimit = 1024;   // a magic glow cycles through 32 phases per button state

    private static void Clear()
    {
        foreach (Bitmap old in Cache.Values) old.Dispose();
        Cache.Clear();
        CacheOrder.Clear();
    }

    /// <summary>The face bitmap (cached; do not dispose it), or null when Direct3D is unavailable (the caller paints flat) — also for a control of your own.</summary>
    public static Bitmap? Draw(AiFgfFace face)
    {
        if (Cache.TryGetValue(face, out Bitmap? cached)) return cached;
        if (AiFgfFaceRenderer.Render(face) is not { } pixels) return null;
        var bmp = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        BitmapData bits = bmp.LockBits(new Rectangle(0, 0, pixels.Width, pixels.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < pixels.Height; y++)
                Marshal.Copy(pixels.Bgra, y * pixels.Width * 4, bits.Scan0 + y * bits.Stride, pixels.Width * 4);
        }
        finally { bmp.UnlockBits(bits); }
        Cache[face] = bmp;
        CacheOrder.Enqueue(face);
        if (Cache.Count > CacheLimit)
        {
            // evict the oldest quarter instead of clearing everything: a brief burst of distinct faces (the pointer
            // sweeping a Raised button, several buttons easing at once) should not force every other button to redraw
            for (int i = 0, evict = CacheLimit / 4; i < evict && CacheOrder.Count > 0; i++)
            {
                AiFgfFace old = CacheOrder.Dequeue();
                if (Cache.Remove(old, out Bitmap? gone)) gone.Dispose();
            }
        }
        return bmp;
    }
}
