#nullable enable
using T3.Graphics.Compat;

namespace Lib.render.culling;

// One workspace per camera. Sequential DrawMesh calls reuse the depth pyramid,
// compaction buffers and shaders. Bounds are rebuilt from the current mesh.
internal sealed class MeshOcclusionRenderer : IDisposable
{
    private readonly Instance _owner;
    private readonly CullDrawStats _stats = new();

    public MeshOcclusionRenderer(Instance owner)
    {
        _owner = owner;
        RenderStatsCollector.RegisterProvider(_stats);
    }

    private const string ShaderPath = "Lib:shaders/3d/mesh/culling/Frustum triangles.hlsl";
    private Resource<ComputeShader>? _prefix, _scatter, _buildBounds, _markDepth;
    private readonly BufferWithViews _masks = new(), _offsets = new(), _visible = new(), _args = new(), _bounds = new();
    private Buffer? _constants, _indirect;
    private readonly MeshDepthPyramid _meshDepth = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public Matrix4x4 ObjectToClip;
        public uint FaceCount, GroupCount, GroupsX, VertexCount;
        public uint DepthWidth, DepthHeight;
        public float DepthBias;
        public uint Padding;
    }

    public void Draw(EvaluationContext context, MeshBuffers mesh, bool opaque)
    {
        var dc = ResourceManager.Device.ImmediateContext;
        int faceCount = mesh.FaceCount;
        int vertices = mesh.VertexBuffer?.ElementCount ?? 0;
        var vertexView = mesh.VertexBuffer?.Srv;
        var indexView = mesh.IndicesBuffer?.Srv;
        bool valid = vertexView is { IsDisposed: false } && indexView is { IsDisposed: false }
                     && mesh.VertexBuffer!.ElementStride == T3.Core.Rendering.PbrVertex.Stride
                     && mesh.IndicesBuffer!.ElementStride == 12 && vertices > 0;
        if (!valid)
        {
            FullDraw(dc, faceCount);
            return;
        }

        int groups = 1 + (faceCount - 1) / 128;
        int rows = 1 + (groups - 1) / 65535;
        int columns = 1 + (groups - 1) / rows;
        var builder = (_buildBounds ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, _owner, () => "BuildBounds")).Value;
        var mark = (_markDepth ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, _owner, () => "MarkDepth")).Value;
        var prefix = (_prefix ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, _owner, () => "Prefix")).Value;
        var scatter = (_scatter ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, _owner, () => "Scatter")).Value;
        if (builder == null || mark == null || prefix == null || scatter == null || !PrepareBuffers(faceCount, groups)
            || !_meshDepth.Build(context, _owner, faceCount, opaque))
        {
            FullDraw(dc, faceCount);
            return;
        }

        var matrix = context.ObjectToWorld * context.WorldToCamera * context.CameraToClipSpace;
        ResourceManager.SetupConstBuffer(new Parameters
        {
            ObjectToClip = Matrix4x4.Transpose(matrix), FaceCount = (uint)faceCount, VertexCount = (uint)vertices,
            GroupCount = (uint)groups, GroupsX = (uint)columns,
            DepthWidth = (uint)_meshDepth.Width, DepthHeight = (uint)_meshDepth.Height, DepthBias = MeshDepthPyramid.DepthBias
        }, ref _constants);

        dc.PushState(StateGroups.ComputeShader);
        try
        {
            var cs = dc.ComputeShader;
            cs.SetConstantBuffer(0, _constants);
            cs.SetShaderResource(0, vertexView);
            cs.SetShaderResource(1, indexView);
            cs.SetShaderResource(2, _meshDepth.Levels);
            cs.SetUnorderedAccessView(0, _masks.Uav, -1);
            cs.SetUnorderedAccessView(1, _offsets.Uav, -1);
            cs.SetUnorderedAccessView(2, _visible.Uav, -1);
            cs.SetUnorderedAccessView(3, _args.Uav, -1);
            cs.SetUnorderedAccessView(4, _bounds.Uav, -1);

            cs.Set(builder);
            dc.Dispatch(columns, rows, 1);
            cs.Set(mark);
            dc.Dispatch(columns, rows, 1);
            cs.Set(prefix);
            dc.Dispatch(1, 1, 1);
            cs.Set(scatter);
            dc.Dispatch(columns, rows, 1);
        }
        finally { dc.PopState(); }
        // Keep transfer reads of the generated packet separate from indirect and uniform reads.
        dc.CopyResource(_args.Buffer!, _indirect!);
        _stats.CountIndirect(dc, _args.Buffer!, faceCount, opaque ? faceCount : 0);
        dc.PushState(StateGroups.VertexShader);
        try
        {
            dc.VertexShader.SetShaderResource(1, _visible.Srv);
            dc.DrawInstancedIndirect(_indirect!, 0);
        }
        finally { dc.PopState(); }
    }

    private void FullDraw(DeviceContext dc, int faces, int depthFaces = 0)
    {
        dc.Draw(checked(faces * 3), 0);
        _stats.CountDirect(faces, depthFaces);
    }

    private bool PrepareBuffers(int faces, int groups)
    {
        // Grow the camera workspace only when the current mesh exceeds its capacity.
        if (_args.Uav == null)
        {
            _args.Buffer ??= new Buffer(ResourceManager.Device, new BufferDescription
            {
                SizeInBytes = 16,
                StructureByteStride = sizeof(uint),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.UnorderedAccess,
                OptionFlags = ResourceOptionFlags.BufferStructured
            });
            _indirect ??= new Buffer(ResourceManager.Device, new BufferDescription
            {
                SizeInBytes = 16, Usage = ResourceUsage.Default, OptionFlags = ResourceOptionFlags.DrawIndirectArguments
            });
            ResourceManager.CreateStructuredBufferUav(_args.Buffer, UnorderedAccessViewBufferFlags.None, ref _args.Uav);
        }
        return _args.Uav != null && Workspace(_masks, groups, 16, false)
               && Workspace(_offsets, groups, 4, false) && Workspace(_visible, faces, 12, true)
               && Workspace(_bounds, groups, 32, false);
    }

    private static bool Workspace(BufferWithViews buffer, int count, int stride, bool read)
    {
        if (buffer.ElementCount < count || buffer.Uav == null || read && buffer.Srv == null)
        {
            int oldCapacity = buffer.ElementCount;
            int capacity = (int)Math.Min(int.MaxValue / stride, Math.Max((long)count, oldCapacity + oldCapacity / 4L));
            if (capacity < count) return false;
            ResourceManager.SetupStructuredBuffer(capacity * stride, stride, ref buffer.Buffer);
            ResourceManager.CreateStructuredBufferUav(buffer.Buffer, UnorderedAccessViewBufferFlags.None, ref buffer.Uav);
            if (read) ResourceManager.CreateStructuredBufferSrv(buffer.Buffer, ref buffer.Srv);
        }
        return buffer.Buffer != null && buffer.ElementCount >= count && buffer.Uav != null && (!read || buffer.Srv != null);
    }

    public void Dispose()
    {
        RenderStatsCollector.UnregisterProvider(_stats);
        _stats.Dispose();
        _masks.Dispose(); _offsets.Dispose(); _visible.Dispose(); _args.Dispose(); _bounds.Dispose();
        _indirect?.Dispose(); _constants?.Dispose();
        _meshDepth.Dispose();
        _prefix?.Dispose(); _scatter?.Dispose(); _markDepth?.Dispose(); _buildBounds?.Dispose();
    }
}
