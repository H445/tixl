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
        public uint PointCount;
        public float PointSize;
        public int ScaleMode, UsePointScale, Padding;
    }

    private readonly Dictionary<ISlot, bool> _depthCommands = new();

    public bool Begin(EvaluationContext context, ISlot command)
    {
        _depthCommands.Clear();
        return VisitsMesh(command) && _meshDepth.Begin(context, _owner);
    }

    // Follow command connections, including the selected branches of flow operators
    // when they evaluate. Independent commands and nested camera passes run only in
    // the normal traversal. Cache reachability for this evaluation (graphs are editable).
    public bool VisitsMesh(ISlot slot)
    {
        if (_depthCommands.TryGetValue(slot, out bool visits)) return visits;
        _depthCommands[slot] = false;
        if (slot is MultiInputSlot<Command> multi)
        {
            foreach (var child in multi.GetCollectedTypedInputs())
                if (VisitsMesh(child)) { visits = true; break; }
        }
        else if (slot is IInputSlot)
        {
            visits = slot.TryGetFirstConnection(out var source) && VisitsMesh(source);
        }
        else if (slot.Parent is Lib.mesh.draw.DrawMesh or Lib.mesh.draw.DrawMeshAtPoints)
        {
            visits = true;
        }
        else if (slot.Parent is not ICamera)
        {
            visits = slot.TryGetFirstConnection(out var source) && VisitsMesh(source);
            if (!visits)
                foreach (var input in slot.Parent.Inputs)
                    if (input.ValueType == typeof(Command) && VisitsMesh(input)) { visits = true; break; }
        }
        _depthCommands[slot] = visits;
        return visits;
    }

    public void FinishDepth() => _meshDepth.Reduce();

    public void DrawDepth(EvaluationContext context, MeshBuffers mesh, BufferWithViews? points, float size, int scaleMode, bool usePointScale)
    {
        var depthShader = points == null ? null : (_pointDepthShader ??= ResourceManager.CreateShaderResource<VertexShader>(
            PointShaderPath, _owner, () => "vsPointDepth")).Value;
        if (_meshDepth.AddOpaqueDraw(context, mesh, points?.ElementCount ?? 1, depthShader))
            _stats.CountDepthDraw();
    }

    public void Draw(EvaluationContext context, MeshBuffers mesh, BufferWithViews? points, float size, int scaleMode, bool usePointScale)
    {
        var dc = ResourceManager.Device.ImmediateContext;
        int faceCount = mesh.FaceCount;
        int submittedFaces = faceCount * (points?.ElementCount ?? 1);
        int vertices = mesh.VertexBuffer?.ElementCount ?? 0;
        var vertexView = mesh.VertexBuffer?.Srv;
        var indexView = mesh.IndicesBuffer?.Srv;
        bool valid = vertexView is { IsDisposed: false } && indexView is { IsDisposed: false }
                     && mesh.VertexBuffer!.ElementStride == T3.Core.Rendering.PbrVertex.Stride
                     && mesh.IndicesBuffer!.ElementStride == 12 && vertices > 0;
        if (!valid)
        {
            FullDraw(dc, submittedFaces);
            return;
        }

        int groups = 1 + (faceCount - 1) / 128;
        int rows = 1 + (groups - 1) / 65535;
        int columns = 1 + (groups - 1) / rows;
        var builder = (_buildBounds ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, _owner, () => "BuildBounds")).Value;
        if (points != null)
        {
            if (!DrawInstances(context, mesh, points, size, scaleMode, usePointScale, builder, groups, columns, rows))
                FullDraw(dc, submittedFaces);
            return;
        }
        var mark = (_markDepth ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, _owner, () => "MarkDepth")).Value;
        var prefix = (_prefix ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, _owner, () => "Prefix")).Value;
        var scatter = (_scatter ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, _owner, () => "Scatter")).Value;
        if (builder == null || mark == null || prefix == null || scatter == null || !PrepareBuffers(faceCount, groups)
            || !_meshDepth.MatchesDraw(context))
        {
            FullDraw(dc, submittedFaces);
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
        dc.PushState(StateGroups.VertexShader);
        try
        {
            dc.VertexShader.SetShaderResource(1, _visible.Srv);
            dc.DrawInstancedIndirect(_indirect!, 0);
            _stats.CountIndirect(dc, _indirect!, faceCount);
        }
        finally { dc.PopState(); }
    }

    private Resource<ComputeShader>? _pointVisibility;
    private const string PointShaderPath = "Lib:shaders/3d/mesh/mesh-DrawAtPoints.hlsl";
    private Resource<VertexShader>? _pointVertexShader, _pointDepthShader;
    private readonly BufferWithViews _pointIds = new();

    private bool DrawInstances(EvaluationContext context, MeshBuffers mesh, BufferWithViews points, float size,
                               int scaleMode, bool usePointScale, ComputeShader? buildBounds, int groups, int columns, int rows)
    {
        var visibility = (_pointVisibility ??= ResourceManager.CreateShaderResource<ComputeShader>(
            ShaderPath, _owner, () => "CullPoints")).Value;
        var vertexShader = (_pointVertexShader ??= ResourceManager.CreateShaderResource<VertexShader>(
            PointShaderPath, _owner, () => "vsVisiblePoints")).Value;
        if (buildBounds == null || visibility == null || vertexShader == null || !_meshDepth.MatchesDraw(context)
            || !PrepareArguments() || !Workspace(_bounds, groups, 32, false)
            || !Workspace(_pointIds, points.ElementCount, sizeof(uint), true))
            return false;

        var matrix = context.ObjectToWorld * context.WorldToCamera * context.CameraToClipSpace;
        ResourceManager.SetupConstBuffer(new Parameters
        {
            ObjectToClip = Matrix4x4.Transpose(matrix), FaceCount = (uint)mesh.FaceCount,
            VertexCount = (uint)mesh.VertexBuffer.ElementCount, GroupCount = (uint)groups, GroupsX = (uint)columns,
            DepthWidth = (uint)_meshDepth.Width, DepthHeight = (uint)_meshDepth.Height, DepthBias = MeshDepthPyramid.DepthBias,
            PointCount = (uint)points.ElementCount, PointSize = size, ScaleMode = scaleMode, UsePointScale = usePointScale ? 1 : 0
        }, ref _constants);
        var dc = ResourceManager.Device.ImmediateContext;
        dc.PushState(StateGroups.ComputeShader);
        try
        {
            var cs = dc.ComputeShader;
            cs.SetConstantBuffer(0, _constants);
            cs.SetShaderResource(0, mesh.VertexBuffer.Srv);
            cs.SetShaderResource(1, mesh.IndicesBuffer.Srv);
            cs.SetShaderResource(2, _meshDepth.Levels);
            cs.SetShaderResource(3, points.Srv);
            cs.SetUnorderedAccessView(1, _pointIds.Uav, -1);
            cs.SetUnorderedAccessView(3, _args.Uav, -1);
            cs.SetUnorderedAccessView(4, _bounds.Uav, -1);
            cs.Set(buildBounds);
            dc.Dispatch(columns, rows, 1);
            cs.Set(visibility);
            dc.Dispatch(1, 1, 1);
        }
        finally { dc.PopState(); }
        dc.CopyResource(_args.Buffer!, _indirect!);
        dc.PushState(StateGroups.VertexShader | StateGroups.InputAssembler);
        try
        {
            dc.VertexShader.Set(vertexShader);
            dc.VertexShader.SetShaderResource(10, _pointIds.Srv);
            MeshDepthPyramid.BindMeshIndices(dc, mesh);
            dc.DrawIndexedInstancedIndirect(_indirect!, 0, 1, DeviceContext.DrawIndexedInstancedIndirectArgsStride);
            _stats.CountIndirect(dc, _indirect!, mesh.FaceCount * points.ElementCount);
        }
        finally { dc.PopState(); }
        return true;
    }

    private void FullDraw(DeviceContext dc, int faces)
    {
        dc.Draw(checked(faces * 3), 0);
        _stats.CountDirect(faces);
    }

    private bool PrepareBuffers(int faces, int groups)
    {
        return PrepareArguments() && Workspace(_masks, groups, 16, false)
               && Workspace(_offsets, groups, 4, false) && Workspace(_visible, faces, 12, true)
               && Workspace(_bounds, groups, 32, false);
    }

    private bool PrepareArguments()
    {
        // Grow the camera workspace only when the current mesh exceeds its capacity.
        if (_args.Uav == null)
        {
            _args.Buffer ??= new Buffer(ResourceManager.Device, new BufferDescription
            {
                SizeInBytes = DeviceContext.DrawIndexedInstancedIndirectArgsStride,
                StructureByteStride = sizeof(uint),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.UnorderedAccess,
                OptionFlags = ResourceOptionFlags.BufferStructured
            });
            _indirect ??= new Buffer(ResourceManager.Device, new BufferDescription
            {
                SizeInBytes = DeviceContext.DrawIndexedInstancedIndirectArgsStride, Usage = ResourceUsage.Default, OptionFlags = ResourceOptionFlags.DrawIndirectArguments
            });
            ResourceManager.CreateStructuredBufferUav(_args.Buffer, UnorderedAccessViewBufferFlags.None, ref _args.Uav);
        }
        return _args.Uav != null;
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
        _pointDepthShader?.Dispose(); _pointIds.Dispose(); _pointVertexShader?.Dispose(); _pointVisibility?.Dispose();
        _prefix?.Dispose(); _scatter?.Dispose(); _markDepth?.Dispose(); _buildBounds?.Dispose();
    }
}
