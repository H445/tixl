namespace Lib.render.culling;

[Guid("a7c4a43f-240b-5474-865c-e72fac3302ef")]
internal sealed class _CullMeshInput : Instance<_CullMeshInput>, ICompoundWithUpdate
{
    [Input(Guid = "01beb8c9-c537-54c1-bd64-b18a5df81697")]
    public readonly MultiInputSlot<MeshBuffers> Meshes = new();

    [Output(Guid = "a259c8e4-a88d-5e58-8961-35effe17a21f")]
    public readonly Slot<MeshBuffers> Result = new();

    public _CullMeshInput() => Result.UpdateAction = SelectMesh;

    private void SelectMesh(EvaluationContext context)
    {
        if (context.ObjectVariables.TryGetValue(CullMesh.DirectMeshesKey, out var value) && value is List<MeshBuffers> direct)
        {
            Result.Value = direct[0];
            return;
        }
        var inputs = Meshes.GetCollectedTypedInputs();
        if (inputs.Count == 1)
            Result.Value = inputs[0].GetValue(context);
        else if (inputs.Count > 1)
            Result.ConnectedUpdate(context);
        else
            Result.Value = null;
    }
}
