using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using Color = System.Drawing.Color;

namespace AI_FGF.AvaloniaDirect3D;

/// <summary>
/// Direct3D 12 button/field faces for the Avalonia front end on Windows - the same pixel shader as
/// AI_FGF's WinForms <c>AiFgfDirect3D</c> (copied, not shared, so this experimental add-on cannot
/// affect the WinForms renderer), producing an Avalonia <see cref="WriteableBitmap"/> instead of a
/// GDI one. Windows-only and opt-in: AI_FGF.Avalonia itself stays cross-platform and uses
/// <c>AiFgfButtonEffects</c> (drop shadows) everywhere; reach for this only in an app that ships on
/// Windows alone and wants the identical look WinForms gets with Direct3D 12.
/// <para>
/// Same fallback story as WinForms: no Direct3D 12 (no GPU/driver, any other OS, a lost device) and
/// <see cref="IsAvailable"/> turns false for good; <see cref="Draw"/> returns null and the caller
/// should fall back to a flat look (<c>AI_FGF_D3D=off</c> forces this, <c>AI_FGF_D3D=warp</c> skips the
/// hardware adapter).
/// </para>
/// </summary>
public static class AiFgfD3D
{
    private static Renderer? _renderer;
    private static bool _tried;

    /// <summary>"Direct3D 12 - &lt;adapter&gt;", "Direct3D 12 - WARP", or why it is off.</summary>
    public static string Status { get; private set; } = "not started";

    /// <summary>True while faces can be drawn (starts the device on first use).</summary>
    public static bool IsAvailable => Start() is not null;

    /// <summary>Faces drawn by the GPU so far (diagnostics).</summary>
    public static int Renders { get; private set; }

    /// <summary>Turn the renderer off; a later <see cref="Restart"/> may bring it back.</summary>
    public static void Disable(string reason)
    {
        _renderer?.Dispose();
        _renderer = null;
        _tried = true;
        Status = "off: " + reason;
        Cache.Clear();
        CacheOrder.Clear();
    }

    /// <summary>Forget a failure and try to start again on next use.</summary>
    public static void Restart() { Disable("restarting"); _tried = false; Status = "not started"; }

    private static Renderer? Start()
    {
        if (_tried) return _renderer;
        _tried = true;
        string mode = Environment.GetEnvironmentVariable("AI_FGF_D3D")?.Trim().ToLowerInvariant() ?? "";
        if (mode == "off") { Status = "off: AI_FGF_D3D=off"; return null; }
        if (!OperatingSystem.IsWindows()) { Status = "off: Direct3D 12 needs Windows"; return null; }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) { Status = "off: Direct3D 12 needs Windows 10 or later"; return null; }
        try
        {
            _renderer = new Renderer(preferWarp: mode == "warp");
            Status = "Direct3D 12 - " + _renderer.Adapter;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _renderer = null;
            Status = "off: " + Reason(ex);
        }
        return _renderer;
    }

    // ------------------------------------------------------------------ faces

    /// <summary>Same fields as WinForms' AiFgfDirect3D.Face: size in device pixels, DPI scale, colours, state.</summary>
    public readonly record struct Face(int Width, int Height, float Scale, Color Body, Color Back, Color Border, float Hover, float Pressed,
                                        Color Glow = default, bool Focused = false, float PointerX = -1f, float PointerY = -1f);

    private static readonly Dictionary<Face, WriteableBitmap> Cache = new();
    private static readonly Queue<Face> CacheOrder = new();
    private const int CacheLimit = 384;

    /// <summary>The face bitmap, or null when Direct3D is unavailable (the caller paints flat).</summary>
    public static WriteableBitmap? Draw(Face face)
    {
        if (face.Width < 4 || face.Height < 4 || face.Width > 4096 || face.Height > 4096) return null;
        if (Cache.TryGetValue(face, out WriteableBitmap? cached)) return cached;
        if (Start() is not { } renderer) return null;
        try
        {
            WriteableBitmap bmp = renderer.Render(face);
            Renders++;
            Cache[face] = bmp;
            CacheOrder.Enqueue(face);
            if (Cache.Count > CacheLimit)
            {
                for (int i = 0, evict = CacheLimit / 4; i < evict && CacheOrder.Count > 0; i++)
                {
                    Face old = CacheOrder.Dequeue();
                    if (Cache.Remove(old, out WriteableBitmap? gone)) gone.Dispose();
                }
            }
            return bmp;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Disable(Reason(ex));
            return null;
        }
    }

    private static string Reason(Exception ex) => $"{ex.GetType().Name}: {ex.Message}".Trim();

    // --------------------------------------------------------------- renderer

    private sealed class Renderer : IDisposable
    {
        private const Format PixelFormat = Format.B8G8R8A8_UNorm;   // matches Avalonia's Bgra8888 byte order
        private const int Constants = 28;

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
            public Vector4 Geometry;
            public Vector4 State;
            public Vector4 Body, Back, Border;
            public Vector4 Glow;
            public Vector4 Pointer;
        }

        public WriteableBitmap Render(Face f)
        {
            EnsureTargets(f.Width, f.Height);
            var c = new FaceConstants
            {
                Geometry = new Vector4(f.Width, f.Height, 3.5f * f.Scale, f.Scale),
                State = new Vector4(f.Hover, f.Pressed, f.Border.A / 255f, f.Glow.A > 0 ? 1 : 0),
                Body = Linear(f.Body), Back = Linear(f.Back), Border = Linear(f.Border),
                Glow = Linear(f.Glow) with { W = f.Focused ? 1 : 0 },
                Pointer = new Vector4(f.PointerX, f.PointerY, 0, 0),
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
                if (!_done.WaitOne(TimeSpan.FromSeconds(2))) throw new TimeoutException("the GPU did not finish a face in 2 s");
            }
            _device.DeviceRemovedReason.CheckError();

            var bmp = new WriteableBitmap(new PixelSize(f.Width, f.Height), new global::Avalonia.Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (ILockedFramebuffer fb = bmp.Lock())
            {
                unsafe
                {
                    byte* src = _readback!.Map<byte>(0);
                    try
                    {
                        int pitch = (int)footprints[0].Footprint.RowPitch;
                        byte* dst = (byte*)fb.Address;
                        for (int y = 0; y < f.Height; y++)
                            Buffer.MemoryCopy(src + (long)y * pitch, dst + (long)y * fb.RowBytes, fb.RowBytes, f.Width * 4);
                    }
                    finally { _readback.Unmap(0); }
                }
            }
            return bmp;
        }

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

        /// <summary>Byte-for-byte the same shader as WinForms' AiFgfDirect3D - see that file for the design notes.</summary>
        private const string Shader = """
            cbuffer Face : register(b0) { float4 geo; float4 st; float4 body; float4 back; float4 border; float4 glow; float4 pointer; };
            struct V { float4 pos : SV_Position; };
            V VSMain(uint id : SV_VertexID)
            {
                V o; float2 uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
                return o;
            }
            float box(float2 p, float2 b, float r) { float2 q = abs(p) - b + r; return length(max(q, 0)) + min(max(q.x, q.y), 0) - r; }
            float4 Glowing(float2 p, float2 size, float r, float s, float hover, float pressed, float4 pointer)
            {
                float m = 3.5 * s;
                float2 c = size * 0.5, half = size * 0.5 - m;
                float skew = 0.05 * pressed;
                float ty = saturate((p.y - (c.y - half.y)) / max(2 * half.y, 1));
                float2 pw = float2(c.x + (p.x - c.x) / (1 - skew * (0.5 - ty)), p.y);
                float d = box(pw - c, half, r);
                float focusedLevel = glow.w > 0.5 ? 0.75 : 0.5;
                float level = lerp(lerp(focusedLevel, 1.0, hover), 0.75, pressed);

                float fall = saturate(1 - max(d, 0) / m);
                float3 col = lerp(back.rgb, glow.rgb, pow(fall, 1.4) * level);

                float t = saturate((p.y - (c.y - half.y)) / (2 * half.y));
                float3 base = lerp(body.rgb, glow.rgb, 0.25 * pressed);
                float3 face = lerp(lerp(base, 1, 0.08), base * 0.95, t);
                face += smoothstep(0.25, 0, t) * 0.06 * (1 - pressed);
                float inner = exp(min(d, 0) / (2.2 * s));
                face = lerp(face, glow.rgb, inner * (0.2 + 0.25 * level));

                if (pointer.x >= 0 && d < 0) face += exp(-length(p - pointer.xy) / (16 * s)) * 0.12 * saturate(-d / (2 * s));

                col = lerp(col, face, saturate(0.5 - d));
                float rim = saturate(1 - abs(d + 0.5 * s) / (0.8 * s));
                col = lerp(col, lerp(glow.rgb, 1, 0.45 * level), rim * (0.55 + 0.45 * level));
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
                float skew = 0.05 * pressed;
                float ty = saturate((p.y - (c.y - half.y)) / max(2 * half.y, 1));
                float2 pw = float2(c.x + (p.x - c.x) / (1 - skew * (0.5 - ty)), p.y);
                float d = box(pw - c, half, r);

                float shadow = (1 - smoothstep(-0.5 * s, 2.5 * s, box(p - c - float2(0, lift), half, r))) * 0.20 * (1 - pressed);
                float3 col = back.rgb * (1 - shadow);

                float t = saturate((p.y - (c.y - half.y)) / (2 * half.y));
                float3 base = lerp(body.rgb, 1, 0.05 * hover);
                float3 top = lerp(base, 1, 0.14), bottom = base * 0.91;
                float3 face = lerp(lerp(top, bottom, t), lerp(bottom, top, t), pressed);
                face += smoothstep(0.25, 0, t) * 0.10 * (1 - pressed);

                float e = 0.75;
                float2 g = float2(box(p - c + float2(e, 0), half, r) - box(p - c - float2(e, 0), half, r),
                                  box(p - c + float2(0, e), half, r) - box(p - c - float2(0, e), half, r));
                float2 n = g / max(length(g), 1e-4);
                float rim = saturate(1 + d / (2.5 * s));
                float lit = dot(n, normalize(float2(-0.4, -1)));
                face += rim * lit * 0.12 * (1 - 2 * pressed);

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
