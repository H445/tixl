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

    private readonly CullDrawStats _stats = new();

    [Input(Guid = "1f78cd12-35ee-46f7-a9c5-177b33c1d860")]
    public readonly InputSlot<bool> OpaquePrepass = new();

    public _DrawFrustumTriangles()
    {
        RenderStatsCollector.RegisterProvider(_stats);
        Draw.UpdateAction = Render;
    }

    private void Render(EvaluationContext context)
    {
        var dc = ResourceManager.Device.ImmediateContext;
        if (context.ObjectVariables.TryGetValue(CullMesh.DirectMeshesKey, out var value) && value is List<MeshBuffers> sources)
        {
            dc.PushState(StateGroups.VertexShader);
            try
            {
                foreach (var source in sources)
                {
                    if (source.VertexBuffer?.Srv == null || source.IndicesBuffer?.Srv == null) continue;
                    dc.VertexShader.SetShaderResource(0, source.VertexBuffer.Srv);
                    dc.VertexShader.SetShaderResource(1, source.IndicesBuffer.Srv);
                    FullDraw(dc, source.FaceCount);
                }
            }
            finally { dc.PopState(); }
            return;
        }

        var mesh = Mesh.GetValue(context);
        int faceCount = mesh?.FaceCount ?? 0;
        if (mesh == null || faceCount <= 0 || dc.VertexShader.Shader == null || dc.PixelShader.Shader == null) return;

        if (context.DrawMeshWithOcclusion != null && !BypassCulling.GetValue(context) && DepthTestEnabled.GetValue(context))
            context.DrawMeshWithOcclusion(context, mesh, OpaquePrepass.GetValue(context));
        else
            FullDraw(dc, faceCount);
    }

    private void FullDraw(DeviceContext dc, int faces)
    {
        dc.Draw(checked(faces * 3), 0);
        _stats.CountDirect(faces, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RenderStatsCollector.UnregisterProvider(_stats);
            _stats.Dispose();
        }
        base.Dispose(disposing);
    }
}
