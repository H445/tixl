namespace Examples.Lib.mesh;

[Guid("c9f757ad-fa43-4ce4-abbf-258f754a7efc")]
internal sealed class IndexedCullingExample : Instance<IndexedCullingExample>
{
    [Input(Guid = "d92c5fb4-f411-5109-9f86-65cf76d6340f")]
    public readonly InputSlot<bool> Benchmark = new();

    [Output(Guid = "5d468644-f0b7-4f94-89a3-4ebb42695492")]
    public readonly Slot<Texture2D> Image = new();
}
