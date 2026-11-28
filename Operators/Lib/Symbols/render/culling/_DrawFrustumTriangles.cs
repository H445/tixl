#nullable enable
using T3.Graphics.Compat;

namespace Lib.render.culling;

[Guid("2f28ae59-4990-5a87-9763-17e68320e4b6")]
internal sealed class _DrawFrustumTriangles : Instance<_DrawFrustumTriangles>
{
    [Input(Guid = "54c1c010-d527-5fd8-a15f-8fed02232b62")] public readonly InputSlot<MeshBuffers> Mesh = new();
    [Input(Guid = "6efc720b-0996-588b-9d04-27e494ba1ea5")] public readonly InputSlot<bool> BypassCulling = new();
    [Input(Guid = "8a59453b-31c6-5829-9ac5-e2b913ba23dc")] public readonly InputSlot<bool> CacheStaticMesh = new();

    [Input(Guid = "cd9c95eb-f9ba-53cb-af8c-6cf7e95c8371")] public readonly InputSlot<bool> DepthTestEnabled = new();

    [Output(Guid = "5f280164-2577-50f9-8267-486628daee70", DirtyFlagTrigger = DirtyFlagTrigger.Always)] public readonly Slot<Command> Draw = new();

    private const string ShaderPath = "Lib:shaders/3d/mesh/culling/Frustum triangles.hlsl";
    private Resource<ComputeShader>? _mark, _prefix, _scatter, _small, _buildBounds, _markStatic, _markDepth;
    private readonly BufferWithViews _masks = new(), _offsets = new(), _visible = new(), _args = new(), _bounds = new();
    private Buffer? _constants, _indirect;
    private ShaderResourceView? _lastVertices, _lastFaces;
    private Matrix4x4 _lastMatrix;
    private int _lastFaceCount, _lastVertexCount, _lastVersion;
    private bool _cached, _hasBounds;

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public Matrix4x4 ObjectToClip;
        public uint FaceCount, GroupCount, GroupsX, VertexCount;
        public uint DepthWidth, DepthHeight;
        public float DepthBias;
        public uint Padding;
    }

    public _DrawFrustumTriangles()
    {
        Draw.UpdateAction = Render;
    }

    private void Render(EvaluationContext context)
    {
        var mesh = Mesh.GetValue(context);
        var dc = ResourceManager.Device.ImmediateContext;
        int faceCount = mesh?.FaceCount ?? 0;
        if (mesh == null || faceCount <= 0 || dc.VertexShader.Shader == null || dc.PixelShader.Shader == null) return;

        int vertices = mesh.VertexBuffer?.ElementCount ?? 0;
        var vertexView = mesh.VertexBuffer?.Srv;
        var indexView = mesh.IndicesBuffer?.Srv;
        bool valid = vertexView is { IsDisposed: false } && indexView is { IsDisposed: false }
                     && mesh.VertexBuffer!.ElementStride == T3.Core.Rendering.PbrVertex.Stride
                     && mesh.IndicesBuffer!.ElementStride == 12 && vertices > 0;
        if (BypassCulling.GetValue(context) || !valid || !DepthCullPass.IsVulkanDevice(ResourceManager.Device))
        {
            FullDraw(dc, faceCount);
            return;
        }

        var matrix = context.ObjectToWorld * context.WorldToCamera * context.CameraToClipSpace;
        bool cache = CacheStaticMesh.GetValue(context);
        DepthCullPass.DepthSnapshot? depth = null;
        if (cache && faceCount > 7 * 128 && context.ObjectVariables.TryGetValue(DepthCullPass.ContextKey, out var value)
            && DepthTestEnabled.GetValue(context) && value is DepthCullPass.DepthSnapshot current && current.Applies(context, dc))
            depth = current;
        int version = Mesh.DirtyFlag.SourceVersion;
        bool sameSource = _lastVersion == version && _lastFaceCount == faceCount && _lastVertexCount == vertices
                          && _lastVertices == vertexView && _lastFaces == indexView;
        if (!cache || !sameSource) _hasBounds = false;
        if (!cache || !_cached || _lastMatrix != matrix || !sameSource || depth != null)
        {
            int groups = 1 + (faceCount - 1) / 128;
            int rows = 1 + (groups - 1) / 65535;
            int columns = 1 + (groups - 1) / rows;
            if (!PrepareBuffers(faceCount, groups))
            {
                FullDraw(dc, faceCount);
                return;
            }
            // The small path consumes the same visible index buffer and indirect count.
            bool small=faceCount<=7*128;
            ComputeShader? mark, prefix=null, scatter=null;
            if(small)
                mark=(_small ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath,this,()=>"FilterSmall")).Value;
            else
            {
                mark=(_mark ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath,this,()=>"Mark")).Value;
                prefix=(_prefix ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath,this,()=>"Prefix")).Value;
                scatter=(_scatter ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath,this,()=>"Scatter")).Value;
            }
            if(mark==null || (!small && (prefix==null || scatter==null))) { FullDraw(dc,faceCount);return; }
            // Small meshes have too few coherent groups to justify building a second index.
            bool useBounds = cache && groups >= 8;
            ComputeShader? builder = null;
            if (useBounds)
            {
                builder = (_buildBounds ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, this, () => "BuildBounds")).Value;
                var grouped = (_markStatic ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, this, () => "MarkStatic")).Value;
                useBounds = builder != null && grouped != null && Workspace(_bounds, groups, 32, false);
                if (useBounds) mark = grouped;
                if (useBounds && depth != null)
                {
                    var depthShader = (_markDepth ??= ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, this, () => "MarkDepth")).Value;
                    if (depthShader != null) mark = depthShader;
                    else depth = null;
                }
            }
            ResourceManager.SetupConstBuffer(new Parameters
            {
                ObjectToClip = Matrix4x4.Transpose(matrix), FaceCount = (uint)faceCount, VertexCount = (uint)vertices,
                GroupCount = (uint)groups, GroupsX = (uint)columns,
                DepthWidth = (uint)(depth?.Width ?? 0), DepthHeight = (uint)(depth?.Height ?? 0), DepthBias = depth?.Bias ?? 0
            }, ref _constants);

            dc.PushState(StateGroups.ComputeShader);
            try
            {
                var cs = dc.ComputeShader;
                cs.SetConstantBuffer(0, _constants);
                cs.SetShaderResource(0, vertexView);
                cs.SetShaderResource(1, indexView);
                if (depth != null) cs.SetShaderResource(2, depth.Levels);
                if(!small)
                {
                    cs.SetUnorderedAccessView(0, _masks.Uav, -1);
                    cs.SetUnorderedAccessView(1, _offsets.Uav, -1);
                }
                cs.SetUnorderedAccessView(2, _visible.Uav, -1);
                cs.SetUnorderedAccessView(3, _args.Uav, -1);
                if (useBounds)
                {
                    cs.SetUnorderedAccessView(4, _bounds.Uav, -1);
                    if (!_hasBounds)
                    {
                        cs.Set(builder);
                        dc.Dispatch(columns, rows, 1);
                        _hasBounds = true;
                    }
                }
                cs.Set(mark);
                dc.Dispatch(small?1:columns, small?1:rows, 1);
                if(!small)
                {
                    cs.Set(prefix);
                    dc.Dispatch(1, 1, 1);
                    cs.Set(scatter);
                    dc.Dispatch(columns, rows, 1);
                }
            }
            finally { dc.PopState(); }
            // Keep transfer reads of the generated packet separate from indirect and uniform reads.
            dc.CopyResource(_args.Buffer!, _indirect!);
            _lastMatrix = matrix;
            _lastVersion = version;
            _lastFaceCount = faceCount;
            _lastVertexCount = vertices;
            _lastVertices = vertexView;
            _lastFaces = indexView;
            _cached = cache && depth == null;
        }
        dc.PushState(StateGroups.VertexShader);
        try
        {
            dc.VertexShader.SetShaderResource(1, _visible.Srv);
            dc.DrawInstancedIndirect(_indirect!, 0);
        }
        finally { dc.PopState(); }
    }

    private void FullDraw(DeviceContext dc, int faces)
    {
        _cached = false;
        _hasBounds = false;
        dc.Draw(checked(faces * 3), 0);
    }

    private bool PrepareBuffers(int faces, int groups)
    {
        // The compute result stays on the GPU. Cached draws reuse the last command snapshot.
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
               && Workspace(_offsets, groups, 4, false) && Workspace(_visible, faces, 12, true);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _masks.Dispose(); _offsets.Dispose(); _visible.Dispose(); _args.Dispose(); _bounds.Dispose();
            _indirect?.Dispose(); _constants?.Dispose();
            _mark?.Dispose(); _small?.Dispose(); _prefix?.Dispose(); _scatter?.Dispose(); _markStatic?.Dispose(); _markDepth?.Dispose(); _buildBounds?.Dispose();
        }
        base.Dispose(disposing);
    }

}
