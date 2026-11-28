#nullable enable
using T3.Graphics.Compat;

namespace Lib.render.culling;

[Guid("64cd0588-51c2-5716-ab93-6fcaa0babde8")]
internal sealed class DepthCullPass : Instance<DepthCullPass>
{
    [Input(Guid = "85dc5b6a-fe05-5606-9c2f-fd1f20c90b83")] public readonly InputSlot<Command> Candidates = new();
    [Input(Guid = "bde3a9df-83ec-5921-a67e-731d91e6848d")] public readonly InputSlot<bool> Enabled = new();
    [Input(Guid = "5c195918-a7fc-51c0-adc6-0b5d0f71b92b")] public readonly InputSlot<float> DepthBias = new();

    [Output(Guid = "6a81fee5-fce2-5b5c-a882-e4a2bfbf8ac6", DirtyFlagTrigger = DirtyFlagTrigger.Always)] public readonly Slot<Command> Draw = new();
    [Output(Guid = "dec2e08c-eb5d-5805-accb-eacb7e5b4167", DirtyFlagTrigger = DirtyFlagTrigger.Always)] public readonly Slot<string> DepthStatus = new();

    internal static bool IsVulkanDevice(Device device) =>
        device.Backend.BackendDescription.StartsWith("Vulkan ", StringComparison.OrdinalIgnoreCase);

    internal const string ContextKey = "Lib.render.culling.OcclusionDepth";
    private const string Shader = "Lib:shaders/3d/mesh/culling/Occlusion depth.hlsl";
    private readonly BufferWithViews _levels = new();
    private T3.Graphics.Compat.Texture2D? _copy, _viewedSource;
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
    private readonly DepthSnapshot _snapshot = new();
    private Device? _formatDevice;
    private bool _canSampleDepth;
    private string _status = "Draw the pass to inspect its depth source.";

    internal sealed class DepthSnapshot
    {
        public ShaderResourceView? Levels;
        public DepthStencilView? Attachment;
        public Matrix4x4 View, Projection;
        public int Width, Height;
        public float Bias;

        public bool Applies(EvaluationContext context, DeviceContext dc)
        {
            var vp = dc.Rasterizer.GetViewport();
            var state = dc.OutputMerger.DepthStencilState?.Description;
            var rasterizer = dc.Rasterizer.State?.Description;
            return Levels is { IsDisposed: false } && dc.OutputMerger.DepthStencilTarget == Attachment
                   && context.WorldToCamera == View && context.CameraToClipSpace == Projection
                   && vp.X == 0 && vp.Y == 0 && vp.Width == Width && vp.Height == Height
                   && vp.MinDepth == 0 && vp.MaxDepth == 1
                   && (!rasterizer.HasValue || (rasterizer.Value.DepthBias == 0 && rasterizer.Value.SlopeScaledDepthBias == 0))
                   && (!state.HasValue || (state.Value.IsDepthEnabled && !state.Value.IsStencilEnabled
                       && state.Value.DepthComparison is Comparison.Less or Comparison.LessEqual));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct Reduction
    {
        public uint Width, Height, SourceStart, TargetStart;
        public uint TargetWidth, TargetHeight, SampleCount, LevelCount;
    }

    public DepthCullPass()
    {
        Draw.UpdateAction = Render;
        DepthStatus.UpdateAction = _ => DepthStatus.Value = _status;
    }

    private void Render(EvaluationContext context)
    {
        var values = context.ObjectVariables;
        bool nested = values.TryGetValue(ContextKey, out var previous);
        values.Remove(ContextKey);
        try
        {
            if (Enabled.GetValue(context))
            {
                if (Build(context)) values[ContextKey] = _snapshot;
            }
            else _status = "Depth bypassed.";
            Candidates.GetValue(context);
        }
        finally
        {
            if (nested) values[ContextKey] = previous!;
            else values.Remove(ContextKey);
        }
    }

    private static int Half(int x) => (x + 1) / 2;

    private bool Build(EvaluationContext context)
    {
        var dc = ResourceManager.Device.ImmediateContext;
        if (!IsVulkanDevice(ResourceManager.Device))
        {
            _status = "GPU depth culling requires the Vulkan backend. Using the regular mesh draw.";
            return false;
        }
        var attachment = dc.OutputMerger.DepthStencilTarget;
        var vp = dc.Rasterizer.GetViewport();
        var projection = context.CameraToClipSpace;
        if (attachment?.Resource is not T3.Graphics.Compat.Texture2D source || source.IsDisposed)
        {
            _status = "No depth attachment. Using frustum culling.";
            return false;
        }
        var d = source.Description;
        int w = d.Width, h = d.Height, samples = d.SampleDescription.Count;
        if (d.Format is not (Format.R32_Typeless or Format.D32_Float) || d.ArraySize != 1 || d.MipLevels != 1
            || w <= 0 || h <= 0 || samples < 1 || vp.X != 0 || vp.Y != 0 || vp.MinDepth != 0 || vp.MaxDepth != 1
            || vp.Width != w || vp.Height != h || !(projection.M43 * projection.M34 - projection.M33 * projection.M44 > 0))
        {
            _status = "Depth requires a full viewport and forward D32 depth. Using frustum culling.";
            return false;
        }

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
            _status = "This device cannot sample D32 depth. Using frustum culling.";
            return false;
        }

        var seed = samples == 1
            ? (_seed ??= ResourceManager.CreateShaderResource<ComputeShader>(Shader, this, () => "Seed")).Value
            : (_seedMS ??= ResourceManager.CreateShaderResource<ComputeShader>(Shader, this, () => "SeedMS")).Value;
        var reduce = (_reduce ??= ResourceManager.CreateShaderResource<ComputeShader>(Shader, this, () => "ReduceLevels")).Value;
        if (seed == null || reduce == null || !Prepare(source, d))
        {
            _status = "Depth hierarchy unavailable. Using frustum culling.";
            return false;
        }

        // A normal TiXL Vulkan RenderTarget is already sampleable. A custom attachment
        // without sampled usage needs one matching device-local copy.
        if (_copy != null) dc.CopyResource(source, _copy);
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
            dc.Dispatch(samples==1 ? (width+3)/4 : width, samples==1 ? (height+1)/2 : height, 1);
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
        _snapshot.Levels = _levels.Srv;
        _snapshot.Attachment = attachment;
        _snapshot.Projection = projection;
        _snapshot.View = context.WorldToCamera;
        _snapshot.Width = w; _snapshot.Height = h;
        float bias = DepthBias.GetValue(context);
        _snapshot.Bias = float.IsFinite(bias) ? Math.Clamp(bias, 0, 1) : 1;
        _status = _copy == null ? "Vulkan depth ready. Sampling the current attachment." : "Vulkan depth ready. Sampling a device-local copy.";
        return true;
    }

    private bool Prepare(T3.Graphics.Compat.Texture2D source, Texture2DDescription d)
    {
        if (ReferenceEquals(source, _viewedSource) && _depthView is { IsDisposed: false, GpuView: not null }
            && _levels.Srv != null && _levels.Uav != null)
            return true;

        _depthView?.Dispose();
        _copy?.Dispose();
        _copy = null;
        _viewedSource = null;
        if ((d.BindFlags & BindFlags.ShaderResource) == 0)
        {
            d.Format = Format.R32_Typeless;
            d.BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource;
            d.Usage = ResourceUsage.Default;
            d.CpuAccessFlags = CpuAccessFlags.None;
            d.OptionFlags = ResourceOptionFlags.None;
            _copy = new T3.Graphics.Compat.Texture2D(ResourceManager.Device, d);
        }
        var viewDescription = new ShaderResourceViewDescription
        {
            Format = Format.R32_Float,
            Dimension = d.SampleDescription.Count > 1 ? ShaderResourceViewDimension.Texture2DMultisampled : ShaderResourceViewDimension.Texture2D
        };
        if (d.SampleDescription.Count == 1)
            viewDescription.Texture2D = new ShaderResourceViewDescription.Texture2DResource { MipLevels = 1 };
        _depthView = new ShaderResourceView(ResourceManager.Device, _copy ?? source, viewDescription);
        if (_depthView.GpuView == null) return false;
        int width = (d.Width + 7) / 8, height = (d.Height + 7) / 8, entries = width * height;
        while (width > 2 || height > 2)
        {
            width = Half(width); height = Half(height);
            entries += width * height;
        }
        _viewedSource = source;
        if (_levels.ElementCount == entries && _levels.Srv != null && _levels.Uav != null)
            return true;
        _levels.Dispose();
        ResourceManager.SetupStructuredBuffer(checked(entries * sizeof(float)), sizeof(float), ref _levels.Buffer);
        if (_levels.Buffer == null) return false;
        ResourceManager.CreateStructuredBufferSrv(_levels.Buffer, ref _levels.Srv);
        ResourceManager.CreateStructuredBufferUav(_levels.Buffer, UnorderedAccessViewBufferFlags.None, ref _levels.Uav);
        return _levels.Srv != null && _levels.Uav != null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _depthView?.Dispose(); _copy?.Dispose(); _levels.Dispose(); foreach(var buffer in _constants) buffer?.Dispose();
            _seed?.Dispose(); _seedMS?.Dispose(); _reduce?.Dispose();
        }
        base.Dispose(disposing);
    }

}
