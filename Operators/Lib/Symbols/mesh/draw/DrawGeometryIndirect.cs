#nullable enable
using Lib.Utils;
using T3.Core.Rendering;
using T3.Graphics.Compat;
using DeviceTexture = T3.Graphics.Compat.Texture2D;

namespace Lib.mesh.draw;

[Guid("55dc5089-52d0-46bf-ba08-3967c51267a1")]
[ExportDependencies("CDT.Core.dll")]
internal sealed class DrawGeometryIndirect : Instance<DrawGeometryIndirect>, IStatusProvider
{
    [Output(Guid = "e7858a2a-0fa6-48ef-9903-0eed4fe836ac", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();
    [Input(Guid = "da67e263-1a84-40fc-ac4e-c6a53599921c")]
    public readonly InputSlot<Geometry> Geometry = new();
    [Input(Guid = "4fc9aa3a-91e1-43fb-a441-5aa5eaf28a9b")]
    public readonly InputSlot<Geometry> LowDetail = new();
    [Input(Guid = "f1da9f26-da47-4019-8ac3-48ca073629fe")]
    public readonly InputSlot<float> LodPixels = new();
    [Input(Guid = "6789bf1b-79de-4718-9b76-c84ebcc5d715")]
    public readonly InputSlot<bool> DepthCulling = new();

    private const string ShaderPath = "Lib:shaders/3d/mesh/DrawGeometryIndirect.hlsl";
    private readonly Resource<ComputeShader> _classify, _reduce, _reduceMs;
    private readonly Resource<VertexShader> _vertexShader;
    private readonly Resource<PixelShader> _pixelShader;
    private readonly GeometryMeshCompiler _fine = new(), _coarse = new();
    private Vector4[] _vertexData = [];
    private Int3[] _faceData = [];
    private Part[] _partData = [];
    private Geometry? _lastFine, _lastCoarse;
    private int _fineVersion, _coarseVersion, _partCount;
    private BufferWithViews? _vertices, _parts;
    private readonly BufferWithViews _commands = new(), _tiles = new();
    private Buffer? _indices, _parameters;
    private ShaderResourceView? _depthView;
    private string? _warning;

    public DrawGeometryIndirect()
    {
        Output.UpdateAction = Update;
        _classify = ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, this, () => "classify");
        _reduce = ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, this, () => "reduceDepth");
        _reduceMs = ResourceManager.CreateShaderResource<ComputeShader>(ShaderPath, this, () => "reduceDepthMS");
        _vertexShader = ResourceManager.CreateShaderResource<VertexShader>(ShaderPath, this, () => "vsMain");
        _pixelShader = ResourceManager.CreateShaderResource<PixelShader>(ShaderPath, this, () => "psMain");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Part
    {
        public Vector4 Lower, Upper;
        public int First, Count, LowFirst, LowCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public Matrix4x4 ObjectToClip;
        public Vector2 ViewSize;
        public float LodPixels;
        public int PartCount, TilesX, UseDepth, GroupsX, Padding;
    }

    private void Update(EvaluationContext context)
    {
        var fine = Geometry.GetValue(context);
        var coarse = LowDetail.GetValue(context);
        var threshold = LodPixels.GetValue(context);
        var useDepth = DepthCulling.GetValue(context);
        _warning = null;
        if (fine == null)
            return;
        if (coarse != null && Math.Max(1, coarse.Parts.Length) != Math.Max(1, fine.Parts.Length))
        {
            _warning = "LowDetail needs matching parts in the same order. Drawing full detail.";
            coarse = null;
        }
        if (fine != _lastFine || coarse != _lastCoarse || fine.Version != _fineVersion || (coarse?.Version ?? 0) != _coarseVersion)
        {
            Upload(fine, coarse);
            _lastFine = fine;
            _lastCoarse = coarse;
            _fineVersion = fine.Version;
            _coarseVersion = coarse?.Version ?? 0;
        }
        if (_partCount == 0)
            return;

        var dc = ResourceManager.Device.ImmediateContext;
        var view = dc.Rasterizer.GetViewport();
        var depth = dc.OutputMerger.DepthStencilTarget?.Resource as DeviceTexture;
        var cs = _classify.GetValue(context);
        var vs = _vertexShader.GetValue(context);
        var ps = _pixelShader.GetValue(context);
        if (cs == null || vs == null || ps == null)
        {
            _warning = "The indirect draw shaders could not be loaded.";
            return;
        }
        var width = view.Width > 0 ? view.Width : context.RequestedResolution.Width;
        var height = view.Height > 0 ? view.Height : context.RequestedResolution.Height;
        var canSample = useDepth && depth != null && depth.Description.ArraySize == 1
                        && depth.Description.Format is Format.D32_Float or Format.R32_Typeless
                        && (depth.Description.BindFlags & BindFlags.ShaderResource) != 0
                        && view.X == 0 && view.Y == 0 && view.MinDepth == 0 && view.MaxDepth == 1
                        && view.Width == depth.Description.Width && view.Height == depth.Description.Height;
        var ms = canSample && depth!.Description.SampleDescription.Count > 1;
        var reducer = canSample ? (ms ? _reduceMs : _reduce).GetValue(context) : null;
        if (useDepth && reducer == null)
            _warning ??= "Depth culling needs a sampleable full-viewport D32 render target. Frustum culling and LOD are active.";
        if (_depthView != null && (reducer == null || _depthView.Resource != depth))
            GraphicsUtilities.Dispose(ref _depthView);
        var tilesX = ((int)width + 7) / 8;
        var tilesY = ((int)height + 7) / 8;
        var groups = (_partCount + 63) / 64;
        var groupsX = Math.Min(groups, 65535);
        var parameters = new Parameters
        {
            ObjectToClip = context.ObjectToWorld * context.WorldToCamera * context.CameraToClipSpace,
            ViewSize = new Vector2(width, height), LodPixels = threshold, PartCount = _partCount,
            TilesX = tilesX, UseDepth = reducer != null ? 1 : 0, GroupsX = groupsX
        };
        if (_parameters == null)
            ResourceManager.SetupConstBuffer(parameters, ref _parameters);
        else
            ResourceManager.UpdateConstBuffer(parameters, _parameters);

        dc.PushState(StateGroups.All);
        try
        {
            var stage = dc.ComputeShader;
            stage.SetConstantBuffer(0, _parameters);
            if (reducer != null)
            {
                _depthView ??= new ShaderResourceView(ResourceManager.Device, depth!, new ShaderResourceViewDescription
                {
                    Format = Format.R32_Float,
                    Dimension = ms ? ShaderResourceViewDimension.Texture2DMultisampled : ShaderResourceViewDimension.Texture2D,
                    Texture2D = new ShaderResourceViewDescription.Texture2DResource { MipLevels = 1 }
                });
                if (_tiles.ElementCount != tilesX * tilesY)
                {
                    ResourceManager.SetupStructuredBuffer(checked(tilesX * tilesY * 4), 4, ref _tiles.Buffer);
                    ResourceManager.CreateStructuredBufferSrv(_tiles.Buffer, ref _tiles.Srv);
                    ResourceManager.CreateStructuredBufferUav(_tiles.Buffer, UnorderedAccessViewBufferFlags.None, ref _tiles.Uav);
                }
                stage.Set(reducer);
                stage.SetShaderResource(ms ? 2 : 1, _depthView);
                stage.SetUnorderedAccessView(1, _tiles.Uav);
                dc.Dispatch(tilesX, tilesY, 1);
                stage.SetUnorderedAccessView(1, null);
                stage.SetShaderResource(ms ? 2 : 1, null);
            }

            stage.Set(cs);
            stage.SetShaderResource(0, _parts!.Srv);
            stage.SetShaderResource(3, reducer == null ? null : _tiles.Srv);
            stage.SetUnorderedAccessView(0, _commands.Uav);
            dc.Dispatch(groupsX, (groups + groupsX - 1) / groupsX, 1);
            stage.SetUnorderedAccessView(0, null);

            dc.InputAssembler.InputLayout = null;
            dc.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            dc.InputAssembler.SetIndexBuffer(_indices, Format.R32_UInt, 0);
            dc.VertexShader.Set(vs);
            dc.VertexShader.SetConstantBuffer(0, _parameters);
            dc.VertexShader.SetShaderResource(4, _vertices!.Srv);
            dc.GeometryShader.Set(null);
            dc.PixelShader.Set(ps);
            dc.Rasterizer.State = DefaultRenderingStates.DefaultRasterizerState;
            dc.OutputMerger.DepthStencilState = DefaultRenderingStates.DefaultDepthStencilState;
            dc.OutputMerger.BlendState = DefaultRenderingStates.DisabledBlendState;
            dc.DrawIndexedInstancedIndirect(_commands.Buffer!, 0, _partCount, DeviceContext.DrawIndexedInstancedIndirectArgsStride);
        }
        finally
        {
            dc.PopState();
        }
    }

    private void Upload(Geometry fine, Geometry? coarse)
    {
        _fine.Compile(fine, false);
        if (coarse != null)
            _coarse.Compile(coarse, false);
        _partCount = 0;
        if (_fine.Triangles.Length == 0)
            return;

        var fineVertices = _fine.Vertices.Length;
        Array.Resize(ref _vertexData, checked(2 * (fineVertices + (coarse == null ? 0 : _coarse.Vertices.Length))));
        Array.Resize(ref _faceData, checked(_fine.Triangles.Length + (coarse == null ? 0 : _coarse.Triangles.Length)));
        Array.Resize(ref _partData, _fine.Chunks.Length);
        var vertices = _vertexData;
        var faces = _faceData;
        var parts = _partData;
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = new Part { Lower = new Vector4(float.MaxValue), Upper = new Vector4(float.MinValue) };
        }
        Pack(_fine, 0, 0, false);
        if (coarse != null)
            Pack(_coarse, fineVertices, _fine.Triangles.Length, true);

        // Two float4s per vertex. The shader indexes the two records explicitly.
        ResourceManager.SetupBufferWithViews(vertices, ref _vertices);
        ResourceManager.SetupBufferWithViews(parts, ref _parts);
        var indexData = MemoryMarshal.AsBytes(faces.AsSpan());
        if (_indices?.Description.SizeInBytes == indexData.Length)
        {
            ResourceManager.Device.ImmediateContext.UpdateSubresource(_indices, 0, indexData, 0, 0);
        }
        else
        {
            var indices = new Buffer(ResourceManager.Device, new BufferDescription
            {
                SizeInBytes = indexData.Length, BindFlags = BindFlags.IndexBuffer, Usage = ResourceUsage.Default
            }, indexData);
            _indices?.Dispose();
            _indices = indices;
        }
        if (_commands.ElementCount != parts.Length)
        {
            _commands.Dispose();
            const int stride = DeviceContext.DrawIndexedInstancedIndirectArgsStride;
            _commands.Buffer = new Buffer(ResourceManager.Device, checked(parts.Length * stride), ResourceUsage.Default,
                                          BindFlags.UnorderedAccess, CpuAccessFlags.None,
                                          ResourceOptionFlags.BufferStructured | ResourceOptionFlags.DrawIndirectArguments, stride);
            ResourceManager.CreateStructuredBufferUav(_commands.Buffer, UnorderedAccessViewBufferFlags.None, ref _commands.Uav);
        }
        _partCount = parts.Length;

        void Pack(GeometryMeshCompiler mesh, int vertexBase, int faceBase, bool low)
        {
            for (var i = 0; i < mesh.Vertices.Length; i++)
            {
                ref var v = ref mesh.Vertices[i];
                vertices[(i + vertexBase) * 2] = new Vector4(v.Position, 1);
                vertices[(i + vertexBase) * 2 + 1] = new Vector4(v.ColorRgb, 1);
            }
            for (var i = 0; i < mesh.Triangles.Length; i++)
            {
                var f = mesh.Triangles[i];
                faces[faceBase + i] = new Int3(f.X + vertexBase, f.Y + vertexBase, f.Z + vertexBase);
            }
            for (var i = 0; i < parts.Length; i++)
            {
                var c = mesh.Chunks[i];
                ref var part = ref parts[i];
                var start = (faceBase + c.StartFaceIndex) * 3;
                if (low)
                {
                    part.LowFirst = start;
                    part.LowCount = c.FaceCount * 3;
                }
                else
                {
                    part.First = start;
                    part.Count = c.FaceCount * 3;
                }
                for (int k = c.StartVertexIndex; k < c.StartVertexIndex + c.VertexCount; k++)
                {
                    var position = vertices[(k + vertexBase) * 2];
                    part.Lower = Vector4.Min(part.Lower, position);
                    part.Upper = Vector4.Max(part.Upper, position);
                }
            }
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;
        _parts?.Dispose();
        _vertices?.Dispose();
        _tiles.Dispose();
        _commands.Dispose();
        _indices?.Dispose();
        _parameters?.Dispose();
        _depthView?.Dispose();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _warning == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    string? IStatusProvider.GetStatusMessage() => _warning;
}
