using System;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using Color = System.Drawing.Color;

namespace AI_FGF;

/// <summary>
/// What a face looks like: size in device pixels, DPI scale, colours, state. A non-empty
/// <paramref name="Glow"/> makes it a glowing face; <paramref name="Focused"/> matters only then.
/// </summary>
/// <remarks><paramref name="Magic"/> (.NET 11, glowing faces): the phase 0..1 of the magic light — an iridescent halo,
/// a shimmer sweeping the body, twinkling sparks, a breathing glow; negative (the default) is the plain glow.
/// <paramref name="Radius"/>: the corner radius in device pixels — half the height makes a pill or a circle (a switch's
/// track and knob); negative (the default) is the controls' 3.5 logical pixels.</remarks>
public readonly record struct AiFgfFace(int Width, int Height, float Scale, Color Body, Color Back, Color Border, float Hover, float Pressed,
                                     Color Glow = default, bool Focused = false, float PointerX = -1f, float PointerY = -1f, float Magic = -1f,
                                     float Radius = -1f);

/// <summary>A rendered face: <see cref="Width"/> x <see cref="Height"/> pixels, 4 bytes each in B, G, R, A order, rows packed.</summary>
public sealed record AiFgfFacePixels(int Width, int Height, byte[] Bgra);

/// <summary>
/// The Direct3D 12 renderer of the framework's faces — one pixel shader and one device for every front end: the
/// WinForms controls (<c>AiFgfDirect3D</c>) and the Avalonia add-on (<c>AiFgfD3D</c>) turn its pixels into their own
/// bitmaps. (Each had a copy of the shader and renderer, and the Avalonia copy had fallen behind.)
/// <para>
/// One device serves the app: the first hardware adapter with Direct3D 12 (feature level 11.0, Windows 10 and 11),
/// else WARP, Windows' software rasteriser. If neither starts — no Direct3D 12, a broken driver, a lost device, any
/// error while drawing — <see cref="IsAvailable"/> turns false for good and the front ends paint flat;
/// <see cref="Status"/> says why. <c>AI_FGF_D3D=off</c> disables it, <c>AI_FGF_D3D=warp</c> skips the hardware adapter.
/// </para>
/// </summary>
public static class AiFgfFaceRenderer
{
    private static Renderer? _renderer;
    private static bool _tried;

    /// <summary>"Direct3D 12 — &lt;adapter&gt;", "Direct3D 12 — WARP", or why it is off.</summary>
    public static string Status { get; private set; } = "not started";

    /// <summary>True while faces can be drawn (starts the device on first use).</summary>
    public static bool IsAvailable => Start() is not null;

    /// <summary>Faces drawn by the GPU so far (diagnostics).</summary>
    public static int Renders { get; private set; }

    /// <summary>Raised when the renderer is turned off or restarted: the front ends drop the bitmaps they cached.</summary>
    public static event Action? Reset;

    /// <summary>Turn the renderer off (faces go flat); a later <see cref="Restart"/> may bring it back.</summary>
    public static void Disable(string reason)
    {
        _renderer?.Dispose();
        _renderer = null;
        _tried = true;
        Status = "off: " + reason;
        Reset?.Invoke();
    }

    /// <summary>Forget a failure and try to start again on next use.</summary>
    public static void Restart() { Disable("restarting"); _tried = false; _timeouts = 0; Status = "not started"; }

    private static int _timeouts;
    private const int MaxTimeoutRestarts = 3;

    private static Renderer? Start()
    {
        if (_tried) return _renderer;
        _tried = true;
        string mode = Environment.GetEnvironmentVariable("AI_FGF_D3D")?.Trim().ToLowerInvariant() ?? "";
        if (mode == "off") { Status = "off: AI_FGF_D3D=off"; return null; }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) { Status = "off: Direct3D 12 needs Windows 10 or later"; return null; }
        try
        {
            _renderer = new Renderer(preferWarp: mode == "warp");
            Status = "Direct3D 12 — " + _renderer.Adapter;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _renderer = null;
            Status = "off: " + Reason(ex);
        }
        return _renderer;
    }

    /// <summary>The face's pixels, or null when Direct3D is unavailable or the size is out of range (the caller paints flat).</summary>
    public static AiFgfFacePixels? Render(AiFgfFace face)
    {
        if (face.Width < 4 || face.Height < 4 || face.Width > 4096 || face.Height > 4096) return null;
        if (Start() is not { } renderer) return null;
        try
        {
            AiFgfFacePixels pixels = renderer.Render(face);
            Renders++;
            return pixels;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Disable(Reason(ex));   // a lost device, a driver fault: flat from now on
            // a GPU that only fell behind gets a fresh renderer on the next face, a few times: one slow moment used to
            // leave every 3D control of the process flat for good
            if (ex is TimeoutException && ++_timeouts <= MaxTimeoutRestarts) _tried = false;
            return null;
        }
    }

    private static string Reason(Exception ex) => $"{ex.GetType().Name}: {ex.Message}".Trim();

    // --------------------------------------------------------------- renderer

    private sealed class Renderer : IDisposable
    {
        private const Format PixelFormat = Format.B8G8R8A8_UNorm;   // GDI's and Avalonia's Bgra8888 byte order: rows copy straight into either bitmap
        private const int Constants = 32;

        private readonly ID3D12Device _device;
        private readonly ID3D12CommandQueue _queue;
        private readonly ID3D12CommandAllocator _allocator;
        private readonly ID3D12GraphicsCommandList _list;
        private readonly ID3D12RootSignature _signature;
        private readonly ID3D12PipelineState _pipeline;
        private readonly ID3D12DescriptorHeap _rtvHeap;
        private readonly ID3D12Fence _fence;
        private readonly AutoResetEvent _done = new(false);
        private ulong _fenceValue;
        private ID3D12Resource? _target, _readback;
        private int _targetW, _targetH;

        public string Adapter { get; }

        public Renderer(bool preferWarp)
        {
            using IDXGIFactory4 factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(false);
            ID3D12Device? device = null;
            string adapterName = "";
            if (!preferWarp)
            {
                for (uint i = 0; device is null && factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
                {
                    using (adapter)
                    {
                        AdapterDescription1 desc = adapter!.Description1;
                        if ((desc.Flags & AdapterFlags.Software) != 0) continue;
                        if (D3D12.D3D12CreateDevice(adapter, FeatureLevel.Level_11_0, out ID3D12Device? d).Success) { device = d; adapterName = desc.Description; }
                    }
                }
            }
            if (device is null)
            {
                using IDXGIAdapter warp = factory.EnumWarpAdapter<IDXGIAdapter>();
                D3D12.D3D12CreateDevice(warp, FeatureLevel.Level_11_0, out device).CheckError();
                adapterName = "WARP (software)";
            }
            _device = device!;
            Adapter = adapterName;

            _queue = _device.CreateCommandQueue(CommandListType.Direct);
            _allocator = _device.CreateCommandAllocator(CommandListType.Direct);
            _signature = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None,
                new[] { new RootParameter1(new RootConstants(0, 0, Constants), ShaderVisibility.All) }));
            var pso = new GraphicsPipelineStateDescription
            {
                RootSignature = _signature,
                VertexShader = Compiler.Compile(Shader, "VSMain", "ffbutton.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None).ToArray(),
                PixelShader = Compiler.Compile(Shader, "PSMain", "ffbutton.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None).ToArray(),
                BlendState = BlendDescription.Opaque,
                RasterizerState = RasterizerDescription.CullNone,
                DepthStencilState = DepthStencilDescription.None,
                SampleMask = uint.MaxValue,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetFormats = new[] { PixelFormat },
                SampleDescription = new SampleDescription(1, 0),
            };
            _pipeline = _device.CreateGraphicsPipelineState(pso);
            _list = _device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, _allocator, _pipeline);
            _list.Close();
            _rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, 1, DescriptorHeapFlags.None, 0));
            _fence = _device.CreateFence(0, FenceFlags.None);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FaceConstants
        {
            public Vector4 Geometry;   // width, height, corner radius, scale
            public Vector4 State;      // hover, pressed, border alpha, glowing
            public Vector4 Body, Back, Border;
            public Vector4 Glow;       // glow colour, focused
            public Vector4 Pointer;    // local cursor x, y in device pixels; x < 0 is no highlight (Raised only)
            public Vector4 Magic;      // phase 0..1, on (1) / off (0)
        }

        public AiFgfFacePixels Render(AiFgfFace f)
        {
            EnsureTargets(f.Width, f.Height);
            var c = new FaceConstants
            {
                Geometry = new Vector4(f.Width, f.Height, f.Radius >= 0 ? f.Radius : 3.5f * f.Scale, f.Scale),
                State = new Vector4(f.Hover, f.Pressed, f.Border.A / 255f, f.Glow.A > 0 ? 1 : 0),
                Body = Linear(f.Body), Back = Linear(f.Back), Border = Linear(f.Border),
                Glow = Linear(f.Glow) with { W = f.Focused ? 1 : 0 },
                Pointer = new Vector4(f.PointerX, f.PointerY, 0, 0),
                Magic = new Vector4(Math.Max(0f, f.Magic), f.Magic >= 0 ? 1 : 0, 0, 0),
            };

            _allocator.Reset();
            _list.Reset(_allocator, _pipeline);
            CpuDescriptorHandle rtv = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
            _list.ResourceBarrierTransition(_target!, ResourceStates.CopySource, ResourceStates.RenderTarget);
            _list.OMSetRenderTargets(rtv, null);
            _list.RSSetViewport(new Viewport(0, 0, f.Width, f.Height));
            _list.RSSetScissorRect(f.Width, f.Height);
            _list.SetGraphicsRootSignature(_signature);
            _list.SetGraphicsRoot32BitConstants(0, c, 0);
            _list.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _list.DrawInstanced(3, 1, 0, 0);
            _list.ResourceBarrierTransition(_target!, ResourceStates.RenderTarget, ResourceStates.CopySource);

            ResourceDescription desc = _target!.Description;
            var footprints = new PlacedSubresourceFootPrint[1];
            _device.GetCopyableFootprints(desc, 0, 1, 0, footprints, new uint[1], new ulong[1], out _);
            _list.CopyTextureRegion(new TextureCopyLocation(_readback!, footprints[0]), 0, 0, 0,
                                    new TextureCopyLocation(_target!, 0), new Box(0, 0, 0, f.Width, f.Height, 1));
            _list.Close();
            _queue.ExecuteCommandList(_list);
            _queue.Signal(_fence, ++_fenceValue).CheckError();
            if (_fence.CompletedValue < _fenceValue)
            {
                _fence.SetEventOnCompletion(_fenceValue, _done).CheckError();
                // generous: a software adapter (WARP) on a busy host can take seconds for one face - that is slow, not lost
                if (!_done.WaitOne(TimeSpan.FromSeconds(10))) throw new TimeoutException("the GPU did not finish a button face in 10 s");
            }
            _device.DeviceRemovedReason.CheckError();

            var pixels = new byte[f.Width * 4 * f.Height];
            unsafe
            {
                byte* src = _readback!.Map<byte>(0);
                try
                {
                    int pitch = (int)footprints[0].Footprint.RowPitch;
                    fixed (byte* dst = pixels)
                        for (int y = 0; y < f.Height; y++)
                            Buffer.MemoryCopy(src + (long)y * pitch, dst + (long)y * f.Width * 4, f.Width * 4, f.Width * 4);
                }
                finally { _readback.Unmap(0); }
            }
            return new AiFgfFacePixels(f.Width, f.Height, pixels);
        }

        /// <summary>A target (and its readback buffer) at least this big, grown in 64-px steps.</summary>
        private void EnsureTargets(int w, int h)
        {
            if (_target is not null && w <= _targetW && h <= _targetH) return;
            _target?.Dispose(); _readback?.Dispose();
            _targetW = Math.Max(_targetW, (w + 63) / 64 * 64);
            _targetH = Math.Max(_targetH, (h + 63) / 64 * 64);
            ResourceDescription tex = ResourceDescription.Texture2D(PixelFormat, (uint)_targetW, (uint)_targetH, 1, 1, 1, 0, ResourceFlags.AllowRenderTarget);
            _target = _device.CreateCommittedResource(HeapType.Default, tex, ResourceStates.CopySource, null);
            _device.CreateRenderTargetView(_target, null, _rtvHeap.GetCPUDescriptorHandleForHeapStart());
            _device.GetCopyableFootprints(tex, 0, 1, 0, out ulong bytes);
            _readback = _device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer(bytes, ResourceFlags.None, 0), ResourceStates.CopyDest, null);
        }

        private static Vector4 Linear(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

        public void Dispose()
        {
            _target?.Dispose(); _readback?.Dispose();
            _fence.Dispose(); _rtvHeap.Dispose(); _list.Dispose(); _pipeline.Dispose(); _signature.Dispose();
            _allocator.Dispose(); _queue.Dispose(); _device.Dispose(); _done.Dispose();
        }

        /// <summary>
        /// A full-screen triangle; the pixel shader shapes the button with a rounded-box distance field:
        /// soft shadow under it, body lit from above (lighter top, darker bottom), a bevel from the edge
        /// normal (lit top-left, shaded bottom-right), a 1-px border. Pressed: no shadow, the body sinks
        /// one pixel and the light flips (inset). Hover: a little brighter.
        /// Glowing: the body is inset by the halo's room; the halo fades to the background exactly at the
        /// control's edge (no seam), a hot rim lines the body and an inner glow fades in from it. Idle, focused,
        /// hover and pressed are four glow levels; pressed also floods the body with the glow colour.
        /// </summary>
        private const string Shader = """
            cbuffer Face : register(b0) { float4 geo; float4 st; float4 body; float4 back; float4 border; float4 glow; float4 pointer; float4 magic; };
            struct V { float4 pos : SV_Position; };
            V VSMain(uint id : SV_VertexID)
            {
                V o; float2 uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
                return o;
            }
            // rounded-box signed distance: "Rounded Box - exact" by Inigo Quilez, iquilezles.org/articles/distfunctions2d (MIT, see THIRD-PARTY-NOTICES.md)
            float box(float2 p, float2 b, float r) { float2 q = abs(p) - b + r; return length(max(q, 0)) + min(max(q.x, q.y), 0) - r; }
            // rotate a colour's hue by h radians around the grey axis (the magic glow's iridescence)
            float3 hue(float3 c, float h) { float3 k = float3(0.57735, 0.57735, 0.57735); float co = cos(h); return c * co + cross(k, c) * sin(h) + k * dot(k, c) * (1 - co); }
            float4 Glowing(float2 p, float2 size, float r, float s, float hover, float pressed, float4 pointer)
            {
                float m = 3.5 * s;
                float2 c = size * 0.5, half = size * 0.5 - m;
                float skew = 0.05 * pressed;   // the same perspective skew Raised got (V30): identity at pressed 0, so idle/hover are pixel-identical to before
                float ty = saturate((p.y - (c.y - half.y)) / max(2 * half.y, 1));
                float2 pw = float2(c.x + (p.x - c.x) / (1 - skew * (0.5 - ty)), p.y);
                float d = box(pw - c, half, r);
                float focusedLevel = glow.w > 0.5 ? 0.75 : 0.5;
                float level = lerp(lerp(focusedLevel, 1.0, hover), 0.75, pressed);   // continuous: same four corners as the old ternary chain, smooth between them

                float fall = saturate(1 - max(d, 0) / m);
                // the magic light (magic.y = 1): the halo breathes, its hue drifts around the body like mother-of-pearl
                float on = magic.y, mt = magic.x, tau = 6.2831853;   // mt: the magic phase (t is the face gradient)
                level *= lerp(1, 0.88 + 0.12 * sin(tau * mt), on);
                float ang = atan2(p.y - c.y, (p.x - c.x) * half.y / max(half.x, 1));
                float3 halo = lerp(glow.rgb, saturate(hue(glow.rgb, 0.7 * sin(ang + tau * mt))), on);
                float3 col = lerp(back.rgb, halo, pow(fall, 1.4) * level);

                float t = saturate((p.y - (c.y - half.y)) / (2 * half.y));
                float3 base = lerp(body.rgb, glow.rgb, 0.25 * pressed);
                float3 face = lerp(lerp(base, 1, 0.08), base * 0.95, t);
                face += smoothstep(0.25, 0, t) * 0.06 * (1 - pressed);   // the same top gloss Raised got (V28), a touch subtler since Glow's own gradient already is
                float inner = exp(min(d, 0) / (2.2 * s));
                face = lerp(face, halo, inner * (0.2 + 0.25 * level));
                // a soft band of light sweeping the body once a cycle, a little slanted
                float u = (p.x - c.x + (p.y - c.y) * 0.6) / max(2 * half.x, 1) + 0.5;
                float b = frac(mt) * 1.8 - 0.4;
                face += on * exp(-pow((u - b) / 0.07, 2)) * 0.10 * saturate(-d / (2 * s));

                // the pointer's specular highlight, strictly inside the body (d < 0) so it can never
                // reach the halo or the control's edge, however close the pointer gets to the corner
                if (pointer.x >= 0 && d < 0) face += exp(-length(p - pointer.xy) / (16 * s)) * 0.12 * saturate(-d / (2 * s));

                col = lerp(col, face, saturate(0.5 - d));
                float rim = saturate(1 - abs(d + 0.5 * s) / (0.8 * s));
                col = lerp(col, lerp(halo, 1, 0.45 * level), rim * (0.55 + 0.45 * level));
                // sparks: six points drifting along the halo, each twinkling in its own rhythm; they fade with the halo,
                // so the control's edge stays exactly the background (no seam)
                if (on > 0.5)
                {
                    float spark = 0;
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 1.0471976 + tau * mt * 0.25 + i * 0.37;
                        float2 sp = c + float2(cos(a) * (half.x + 0.45 * m), sin(a) * (half.y + 0.45 * m));
                        float tw = pow(saturate(sin(tau * (mt * 2 + i * 0.37))), 6);
                        spark += exp(-length(p - sp) / (0.9 * s)) * tw;
                    }
                    col = lerp(col, lerp(halo, 1, 0.6), saturate(spark) * 0.85 * fall * step(0, d));
                }
                return float4(saturate(col), 1);
            }
            float4 PSMain(V i) : SV_Target
            {
                float2 size = geo.xy; float r = geo.z; float s = geo.w;
                float hover = st.x, pressed = st.y;
                float2 p = i.pos.xy;
                if (st.w > 0.5) return Glowing(p, size, r, s, hover, pressed, pointer);
                float lift = 1.5 * s;
                float2 half = float2(size.x * 0.5 - 0.5, (size.y - lift) * 0.5 - 0.5);
                float2 c = float2(size.x * 0.5, half.y + 0.5 + pressed * lift * 0.66);
                float skew = 0.05 * pressed;   // pressed: the top narrows slightly as if the face tilts back - real perspective, not just lighting; 0 at pressed 0, so the unpressed look is pixel-identical to before
                float ty = saturate((p.y - (c.y - half.y)) / max(2 * half.y, 1));
                float2 pw = float2(c.x + (p.x - c.x) / (1 - skew * (0.5 - ty)), p.y);
                float d = box(pw - c, half, r);

                float shadow = (1 - smoothstep(-0.5 * s, 2.5 * s, box(p - c - float2(0, lift), half, r))) * 0.20 * (1 - pressed);
                float3 col = back.rgb * (1 - shadow);

                float t = saturate((p.y - (c.y - half.y)) / (2 * half.y));
                float3 base = lerp(body.rgb, 1, 0.05 * hover);
                float3 top = lerp(base, 1, 0.14), bottom = base * 0.91;
                float3 face = lerp(lerp(top, bottom, t), lerp(bottom, top, t), pressed);   // continuous: equals the old branch at pressed 0/1, cross-fades between
                face += smoothstep(0.25, 0, t) * 0.10 * (1 - pressed);   // a soft glassy highlight near the top; only strengthens the lit-from-above look, fades with the rest of it when pressed

                float e = 0.75;
                float2 g = float2(box(p - c + float2(e, 0), half, r) - box(p - c - float2(e, 0), half, r),
                                  box(p - c + float2(0, e), half, r) - box(p - c - float2(0, e), half, r));
                float2 n = g / max(length(g), 1e-4);
                float rim = saturate(1 + d / (2.5 * s));
                float lit = dot(n, normalize(float2(-0.4, -1)));
                face += rim * lit * 0.12 * (1 - 2 * pressed);   // continuous: same +1/-1 at pressed 0/1, fades through 0 instead of snapping

                if (pointer.x >= 0) face += exp(-length(p - pointer.xy) / (20 * s)) * 0.16;

                float cover = saturate(0.5 - d);
                col = lerp(col, face, cover);
                float edge = cover * saturate(d + s + 0.5);
                col = lerp(col, border.rgb, edge * st.z);
                return float4(saturate(col), 1);
            }
            """;
    }
}
