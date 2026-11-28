namespace Examples.Lib.mesh;

[Guid("ebf1836d-ab96-4a6f-95f5-be30bbe2aa2e")]
internal sealed class IndexedLodExample : Instance<IndexedLodExample>
{
    [Input(Guid = "28957b0f-fcf6-5ec8-8e76-e61065ebfa04")]
    public readonly InputSlot<bool> Benchmark = new();

    [Output(Guid = "7472a438-44e6-40ec-9af1-488434a7dfaa")]
    public readonly Slot<Texture2D> Image = new();
}
