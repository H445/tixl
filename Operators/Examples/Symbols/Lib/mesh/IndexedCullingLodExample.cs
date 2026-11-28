namespace Examples.Lib.mesh;

[Guid("f996b4dc-cd7c-4868-b8c5-413600a4036d")]
internal sealed class IndexedCullingLodExample : Instance<IndexedCullingLodExample>
{
    [Output(Guid = "f4c53da0-05c0-40c0-9ed4-af143573e1b7")]
    public readonly Slot<Texture2D> Image = new();
}
