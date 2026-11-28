namespace Lib.render.culling;

[Guid("a9801dd7-f7f7-57d8-a573-bea3d12eeb7b")]
internal sealed class CullMesh : Instance<CullMesh>
{
    [Input(Guid = "e5ef4ff9-92a7-57aa-97be-33379b9520b2")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "9d2a8cf8-12d8-555b-8c7f-0e1c13606134")]
    public readonly InputSlot<Command> Occluders = new();

    [Input(Guid = "3b1e0b01-2558-5d54-a4ac-bef16f14ec74")]
    public readonly InputSlot<bool> StaticMesh = new();

    [Output(Guid = "ce61b3ce-3e77-557a-973b-eaa3f0f3c341")]
    public readonly Slot<Command> Draw = new();
}
