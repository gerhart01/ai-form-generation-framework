using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AI_FGF;

namespace AI_FGF.AvaloniaDirect3D;

/// <summary>
/// Direct3D 12 button/field faces for the Avalonia front end on Windows: <see cref="AiFgfFaceRenderer"/> — the very
/// renderer and pixel shader the WinForms controls use — draws the face, and this turns its pixels into an Avalonia
/// <see cref="WriteableBitmap"/>. (It used to carry its own copy of the shader and renderer, which had fallen behind.)
/// Windows-only and opt-in: AI_FGF.Avalonia itself stays cross-platform and uses <c>AiFgfButtonEffects</c> (drop
/// shadows) everywhere; reach for this only in an app that ships on Windows alone and wants the WinForms look.
/// <para>
/// Same fallback story as WinForms: no Direct3D 12 (no GPU/driver, any other OS, a lost device) and
/// <see cref="IsAvailable"/> turns false for good; <see cref="Draw"/> returns null and the caller paints flat
/// (<c>AI_FGF_D3D=off</c> forces this, <c>AI_FGF_D3D=warp</c> skips the hardware adapter).
/// </para>
/// </summary>
public static class AiFgfD3D
{
    static AiFgfD3D() => AiFgfFaceRenderer.Reset += Clear;

    /// <inheritdoc cref="AiFgfFaceRenderer.Status"/>
    public static string Status => OperatingSystem.IsWindows() ? AiFgfFaceRenderer.Status : "off: Direct3D 12 needs Windows";

    /// <inheritdoc cref="AiFgfFaceRenderer.IsAvailable"/>
    public static bool IsAvailable => OperatingSystem.IsWindows() && AiFgfFaceRenderer.IsAvailable;

    /// <inheritdoc cref="AiFgfFaceRenderer.Renders"/>
    public static int Renders => AiFgfFaceRenderer.Renders;

    /// <summary>Turn the renderer off; a later <see cref="Restart"/> may bring it back.</summary>
    public static void Disable(string reason) => AiFgfFaceRenderer.Disable(reason);

    /// <summary>Forget a failure and try to start again on next use.</summary>
    public static void Restart() => AiFgfFaceRenderer.Restart();

    private static readonly Dictionary<AiFgfFace, WriteableBitmap> Cache = new();
    private static readonly Queue<AiFgfFace> CacheOrder = new();
    private const int CacheLimit = 1024;

    private static void Clear()
    {
        foreach (WriteableBitmap old in Cache.Values) old.Dispose();
        Cache.Clear();
        CacheOrder.Clear();
    }

    /// <summary>The face bitmap, or null when Direct3D is unavailable (the caller paints flat).</summary>
    public static WriteableBitmap? Draw(AiFgfFace face)
    {
        if (Cache.TryGetValue(face, out WriteableBitmap? cached)) return cached;
        if (!OperatingSystem.IsWindows() || AiFgfFaceRenderer.Render(face) is not { } pixels) return null;
        var bmp = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new global::Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (ILockedFramebuffer fb = bmp.Lock())
            for (int y = 0; y < pixels.Height; y++)
                Marshal.Copy(pixels.Bgra, y * pixels.Width * 4, fb.Address + y * fb.RowBytes, pixels.Width * 4);
        Cache[face] = bmp;
        CacheOrder.Enqueue(face);
        if (Cache.Count > CacheLimit)
        {
            for (int i = 0, evict = CacheLimit / 4; i < evict && CacheOrder.Count > 0; i++)
            {
                AiFgfFace old = CacheOrder.Dequeue();
                if (Cache.Remove(old, out WriteableBitmap? gone)) gone.Dispose();
            }
        }
        return bmp;
    }
}
