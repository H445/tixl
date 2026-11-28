namespace Examples.Lib.mesh;

[Guid("c9f757ad-fa43-4ce4-abbf-258f754a7efc")]
internal sealed class IndexedCullingExample : Instance<IndexedCullingExample>
{
    [Output(Guid = "5d468644-f0b7-4f94-89a3-4ebb42695492")]
    public readonly Slot<Texture2D> Image = new();
}
