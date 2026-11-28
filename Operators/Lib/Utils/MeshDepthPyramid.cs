#nullable enable
using T3.Graphics.Compat;

namespace Lib.render.culling;

// Builds a private forward-depth hierarchy for the bound opaque mesh draw.
internal sealed class MeshDepthPyramid : IDisposable
{
    internal static bool IsVulkanDevice(Device device) =>
        device.Backend.BackendDescription.StartsWith("Vulkan ", StringComparison.OrdinalIgnoreCase);

    private const string Shader = "Lib:shaders/3d/mesh/culling/Occlusion depth.hlsl";
    private readonly BufferWithViews _levels = new();
    private T3.Graphics.Compat.Texture2D? _meshDepthTexture, _sceneDepthTexture;
    private ShaderResourceView? _depthView;
    // Constants depend only on the attachment dimensions and the mip stage.
    private readonly Buffer?[] _constants = new Buffer?[12];
    private readonly Reduction[] _descriptors = new Reduction[12];
    private void Bind(T3.Graphics.Compat.DeviceContext dc, int stage, Reduction data)
    {
        ref var buffer=ref _constants[stage];
        if (buffer==null || !_descriptors[stage].Equals(data))
        {
            ResourceManager.SetupConstBuffer(data,ref buffer);
            _descriptors[stage]=data;
        }
        dc.ComputeShader.SetConstantBuffer(0,buffer);
    }
    private Resource<ComputeShader>? _seed, _seedMS, _reduce;

    private Device? _formatDevice;
    private bool _canSampleDepth;
    private DepthStencilView? _depthTarget;

    public ShaderResourceView? Levels => _levels.Srv;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public const float DepthBias = 3e-5f;

    [StructLayout(LayoutKind.Sequential)]
    private record struct Reduction
    {
        public uint Width, Height, SourceStart, TargetStart;
        public uint TargetWidth, TargetHeight, SampleCount, LevelCount;
    }

    private static int Half(int x) => (x + 1) / 2;

    public bool Build(EvaluationContext context, _DrawFrustumTriangles owner, int faceCount)
    {
        var dc = ResourceManager.Device.ImmediateContext;
        var attachment = dc.OutputMerger.DepthStencilTarget;
        var vp = dc.Rasterizer.GetViewport();
        var projection = context.CameraToClipSpace;
        if (attachment?.Resource is not T3.Graphics.Compat.Texture2D source || source.IsDisposed)
        {
            return false;
        }
        var d = source.Description;
        int w = d.Width, h = d.Height, samples = d.SampleDescription.Count;
        if (d.Format is not (Format.R32_Typeless or Format.D32_Float) || d.ArraySize != 1 || d.MipLevels != 1
            || w <= 0 || h <= 0 || samples < 1 || vp.X != 0 || vp.Y != 0 || vp.MinDepth != 0 || vp.MaxDepth != 1
            || vp.Width != w || vp.Height != h || !(projection.M43 * projection.M34 - projection.M33 * projection.M44 > 0))
        {
            return false;
        }

        var state = dc.OutputMerger.DepthStencilState?.Description;
        var rasterizer = dc.Rasterizer.State?.Description;
        if (state.HasValue && (!state.Value.IsDepthEnabled || state.Value.IsStencilEnabled
                               || state.Value.DepthWriteMask != DepthWriteMask.All
                               || state.Value.DepthComparison is not (Comparison.Less or Comparison.LessEqual)))
            return false;
        if (rasterizer.HasValue && (rasterizer.Value.DepthBias != 0 || rasterizer.Value.SlopeScaledDepthBias != 0))
            return false;

        // Depth attachment support alone does not guarantee a sampled D32 view.
        var device = ResourceManager.Device;
        if (!ReferenceEquals(_formatDevice, device))
        {
            var required = FormatSupport.DepthStencil | FormatSupport.ShaderSample;
            _canSampleDepth = (device.CheckFormatSupport(Format.D32_Float) & required) == required;
            _formatDevice = device;
        }
        if (!_canSampleDepth)
        {
            return false;
        }

        var seed = samples == 1
            ? (_seed ??= ResourceManager.CreateShaderResource<ComputeShader>(Shader, owner, () => "Seed")).Value
            : (_seedMS ??= ResourceManager.CreateShaderResource<ComputeShader>(Shader, owner, () => "SeedMS")).Value;
        var reduce = (_reduce ??= ResourceManager.CreateShaderResource<ComputeShader>(Shader, owner, () => "ReduceLevels")).Value;
        if (seed == null || reduce == null || !Prepare(source, d))
        {
            return false;
        }

        // Only the private attachment receives prepass writes. Seed it with existing scene depth,
        // then rasterize all supplied faces before deciding which groups need material shading.
        // CullMesh uses opaque blending and zero alpha cutoff. The bound vertex shader is the
        // material draw's vertex shader. Omitting the fragment shader preserves its raster coverage.
        dc.CopyResource(source, _meshDepthTexture!);
        dc.PushState(StateGroups.OutputMerger | StateGroups.PixelShader);
        try
        {
            dc.OutputMerger.SetTargets(_depthTarget);
            dc.PixelShader.Set(null);
            dc.Draw(checked(faceCount * 3), 0);
        }
        finally
        {
            dc.PopState();
        }
        dc.PushState(StateGroups.ComputeShader);
        try
        {
            var cs = dc.ComputeShader;
            int width = (w + 7) / 8, height = (h + 7) / 8;
            cs.Set(seed);
            cs.SetShaderResource(samples == 1 ? 0 : 1, _depthView);
            cs.SetUnorderedAccessView(0, _levels.Uav, -1);
            Bind(dc, 0, new Reduction
            {
                Width=(uint)w, Height=(uint)h, TargetWidth=(uint)width, TargetHeight=(uint)height, SampleCount=(uint)samples
            });
            dc.Dispatch(samples == 1 ? (width + 3) / 4 : width, samples == 1 ? (height + 1) / 2 : height, 1);
            int stage=0;
            cs.Set(reduce);
            int offset = 0;
            while (width > 2 || height > 2)
            {
                int firstWidth = Half(width), firstHeight = Half(height);
                var reduction = new Reduction
                {
                    Width = (uint)width, Height = (uint)height, SourceStart = (uint)offset,
                    TargetStart = (uint)(offset + width * height),
                    TargetWidth = (uint)firstWidth, TargetHeight = (uint)firstHeight
                };
                // An 8 x 8 workgroup can write four packed levels without another dispatch.
                do
                {
                    offset += width * height;
                    width = Half(width);
                    height = Half(height);
                    reduction.LevelCount++;
                }
                while (reduction.LevelCount < 4 && (width > 2 || height > 2));
                Bind(dc, ++stage, reduction);
                dc.Dispatch((firstWidth + 7) / 8, (firstHeight + 7) / 8, 1);
            }
        }
        finally { dc.PopState(); }
        Width = w;
        Height = h;

        return true;
    }

    private bool Prepare(T3.Graphics.Compat.Texture2D source, Texture2DDescription d)
    {
        if (ReferenceEquals(source, _sceneDepthTexture) && _depthView is { IsDisposed: false, GpuView: not null }
            && _depthTarget?.GpuView != null && _levels.Srv != null && _levels.Uav != null)
            return true;

        _depthTarget?.Dispose();
        _depthView?.Dispose();
        _meshDepthTexture?.Dispose();
        _sceneDepthTexture = null;
        d.Format = Format.R32_Typeless;
        d.BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource;
        d.Usage = ResourceUsage.Default;
        d.CpuAccessFlags = CpuAccessFlags.None;
        d.OptionFlags = ResourceOptionFlags.None;
        _meshDepthTexture = new T3.Graphics.Compat.Texture2D(ResourceManager.Device, d);
        _depthTarget = new DepthStencilView(ResourceManager.Device, _meshDepthTexture, new DepthStencilViewDescription
        {
            Format = Format.D32_Float,
            Dimension = d.SampleDescription.Count > 1 ? DepthStencilViewDimension.Texture2DMultisampled : DepthStencilViewDimension.Texture2D
        });
        var viewDescription = new ShaderResourceViewDescription
        {
            Format = Format.R32_Float,
            Dimension = d.SampleDescription.Count > 1 ? ShaderResourceViewDimension.Texture2DMultisampled : ShaderResourceViewDimension.Texture2D
        };
        if (d.SampleDescription.Count == 1)
            viewDescription.Texture2D = new ShaderResourceViewDescription.Texture2DResource { MipLevels = 1 };
        _depthView = new ShaderResourceView(ResourceManager.Device, _meshDepthTexture, viewDescription);
        if (_depthView.GpuView == null || _depthTarget.GpuView == null) return false;
        int width = (d.Width + 7) / 8, height = (d.Height + 7) / 8, entries = width * height;
        while (width > 2 || height > 2)
        {
            width = Half(width); height = Half(height);
            entries += width * height;
        }
        _sceneDepthTexture = source;
        if (_levels.ElementCount == entries && _depthTarget?.GpuView != null && _levels.Srv != null && _levels.Uav != null)
            return true;
        _levels.Dispose();
        ResourceManager.SetupStructuredBuffer(checked(entries * sizeof(float)), sizeof(float), ref _levels.Buffer);
        if (_levels.Buffer == null) return false;
        ResourceManager.CreateStructuredBufferSrv(_levels.Buffer, ref _levels.Srv);
        ResourceManager.CreateStructuredBufferUav(_levels.Buffer, UnorderedAccessViewBufferFlags.None, ref _levels.Uav);
        return _levels.Srv != null && _levels.Uav != null;
    }

    public void Dispose()
    {
        _depthTarget?.Dispose();
        _depthView?.Dispose();
        _meshDepthTexture?.Dispose();
        _levels.Dispose();
        foreach (var buffer in _constants) buffer?.Dispose();
        _seed?.Dispose();
        _seedMS?.Dispose();
        _reduce?.Dispose();
    }
}
