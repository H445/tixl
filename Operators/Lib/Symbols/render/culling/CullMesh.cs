namespace Lib.render.culling;

[Guid("a9801dd7-f7f7-57d8-a573-bea3d12eeb7b")]
internal sealed class CullMesh : Instance<CullMesh>, ICompoundWithUpdate
{
    [Input(Guid = "e5ef4ff9-92a7-57aa-97be-33379b9520b2")]
    public readonly MultiInputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "3b1e0b01-2558-5d54-a4ac-bef16f14ec74")]
    public readonly InputSlot<bool> StaticMesh = new();

    [Output(Guid = "ce61b3ce-3e77-557a-973b-eaa3f0f3c341")]
    public readonly Slot<Command> Draw = new();

    internal const string DirectMeshesKey = "Lib.render.culling.DirectMeshes";
    private readonly List<MeshBuffers> _directMeshes = new();

    public CullMesh() => Draw.UpdateAction = Render;

    private void Render(EvaluationContext context)
    {
        if (MeshDepthPyramid.IsVulkanDevice(ResourceManager.Device))
        {
            Draw.ConnectedUpdate(context);
            return;
        }

        // The direct backend draws each source in its original buffers with the shared material.
        // Keep the list scoped to this evaluation so a sibling draw cannot inherit it.
        _directMeshes.Clear();
        foreach (var input in Mesh.GetCollectedTypedInputs())
        {
            if (input.GetValue(context) is { FaceCount: > 0 } mesh)
                _directMeshes.Add(mesh);
        }
        if (_directMeshes.Count == 0) return;

        var variables = context.ObjectVariables;
        bool nested = variables.TryGetValue(DirectMeshesKey, out var previous);
        variables[DirectMeshesKey] = _directMeshes;
        try
        {
            Draw.ConnectedUpdate(context);
        }
        finally
        {
            if (nested) variables[DirectMeshesKey] = previous;
            else variables.Remove(DirectMeshesKey);
        }
    }
}
