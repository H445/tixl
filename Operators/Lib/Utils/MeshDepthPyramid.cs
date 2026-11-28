#nullable enable
using T3.Graphics.Compat;

namespace Lib.render.culling;

// One private depth attachment and one reduction for the current camera evaluation.
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

    private Matrix4x4 _view, _projection;

    public bool MatchesDraw(EvaluationContext context)
    {
        var dc = ResourceManager.Device.ImmediateContext;
        var vp = dc.Rasterizer.GetViewport();
        if (!ReferenceEquals(dc.OutputMerger.DepthStencilTarget?.Resource, _sceneDepthTexture)
            || context.WorldToCamera != _view || context.CameraToClipSpace != _projection
            || vp.X != 0 || vp.Y != 0 || vp.MinDepth != 0 || vp.MaxDepth != 1
            || vp.Width != Width || vp.Height != Height)
            return false;

        var state = dc.OutputMerger.DepthStencilState?.Description;
        var rasterizer = dc.Rasterizer.State?.Description;
        return (!state.HasValue || state.Value.IsDepthEnabled && !state.Value.IsStencilEnabled
                                && state.Value.DepthComparison is Comparison.Less or Comparison.LessEqual)
               && (!rasterizer.HasValue || rasterizer.Value.DepthBias == 0 && rasterizer.Value.SlopeScaledDepthBias == 0);
    }

    public bool Begin(EvaluationContext context, Instance owner)
    {
        var dc = ResourceManager.Device.ImmediateContext;
        if (dc.OutputMerger.DepthStencilTarget?.Resource is not T3.Graphics.Compat.Texture2D source || source.IsDisposed)
            return false;

        var d = source.Description;
        _view = context.WorldToCamera;
        _projection = context.CameraToClipSpace;
        if (d.Format is not (Format.R32_Typeless or Format.D32_Float) || d.ArraySize != 1 || d.MipLevels != 1
            || d.Width <= 0 || d.Height <= 0 || d.SampleDescription.Count < 1
            || !(_projection.M43 * _projection.M34 - _projection.M33 * _projection.M44 > 0))
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

        int samples = d.SampleDescription.Count;
        var seed = samples == 1
                       ? (_seed ??= ResourceManager.CreateShaderResource<ComputeShader>(Shader, owner, () => "Seed")).Value
                       : (_seedMS ??= ResourceManager.CreateShaderResource<ComputeShader>(Shader, owner, () => "SeedMS")).Value;
        var reduce = (_reduce ??= ResourceManager.CreateShaderResource<ComputeShader>(Shader, owner, () => "ReduceLevels")).Value;
        if (seed == null || reduce == null || !Prepare(source, d)) return false;

        Width = d.Width;
        Height = d.Height;
        dc.CopyResource(source, _meshDepthTexture!);
        return true;
    }

    // Vulkan structured mesh indices are packed uint3 values and can also serve as an index buffer.
    internal static void BindMeshIndices(DeviceContext dc, MeshBuffers mesh)
    {
        var indices = mesh.IndicesBuffer;
        int offset = indices.Srv!.Description.Buffer.FirstElement * 12;
        dc.InputAssembler.SetIndexBuffer(indices.Buffer, Format.R32_UInt, offset);
    }

    public bool AddOpaqueDraw(EvaluationContext context, MeshBuffers mesh, int copies, VertexShader? pointDepthShader, Buffer? packedIndices, Buffer? packedArguments)
    {
        var dc = ResourceManager.Device.ImmediateContext;
        if (!MatchesDraw(context) || dc.OutputMerger.DepthStencilState?.Description.DepthWriteMask == DepthWriteMask.Zero)
            return false;

        // Preserve the authored rasterizer and placement. Point batches use the indexed, position-only entry.
        dc.PushState(StateGroups.OutputMerger | StateGroups.PixelShader | StateGroups.VertexShader | StateGroups.InputAssembler);
        try
        {
            dc.OutputMerger.SetTargets(_depthTarget);
            dc.PixelShader.Set(null);
            if (pointDepthShader != null)
            {
                dc.VertexShader.Set(pointDepthShader);
                if (packedIndices != null && packedArguments != null)
                {
                    dc.InputAssembler.SetIndexBuffer(packedIndices, Format.R32_UInt, 0);
                    dc.DrawIndexedInstancedIndirect(packedArguments, 0, 1, DeviceContext.DrawIndexedInstancedIndirectArgsStride);
                }
                else
                {
                    BindMeshIndices(dc, mesh);
                    dc.DrawIndexedInstanced(checked(mesh.FaceCount * 3), copies, 0, 0, 0);
                }
            }
            else
                dc.Draw(checked(mesh.FaceCount * 3 * copies), 0);
        }
        finally { dc.PopState(); }
        return true;
    }

    public void Reduce()
    {
        var dc = ResourceManager.Device.ImmediateContext;
        int samples = _sceneDepthTexture!.Description.SampleDescription.Count;
        int w = Width, h = Height;
        var seed = samples == 1 ? _seed!.Value : _seedMS!.Value;
        var reduce = _reduce!.Value;
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
