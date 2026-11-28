namespace Examples.Lib.mesh;

[Guid("ebf1836d-ab96-4a6f-95f5-be30bbe2aa2e")]
internal sealed class IndexedLodExample : Instance<IndexedLodExample>
{
    [Output(Guid = "7472a438-44e6-40ec-9af1-488434a7dfaa")]
    public readonly Slot<Texture2D> Image = new();
}
