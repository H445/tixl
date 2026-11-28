namespace Examples.Lib.mesh;

[Guid("d13d7e22-04f4-4d03-8af6-000000001000")]
internal sealed class DrawGeometryIndirectExample : Instance<DrawGeometryIndirectExample>
{
    [Output(Guid = "d13d7e22-04f4-4d03-8af6-000000001001")]
    public readonly Slot<Texture2D> Image = new();

    [Input(Guid = "d13d7e22-04f4-4d03-8af6-000000001002", MappedType = typeof(DrawMode))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "d13d7e22-04f4-4d03-8af6-000000001003")]
    public readonly InputSlot<bool> Benchmark = new();

    [Input(Guid = "d13d7e22-04f4-4d03-8af6-000000001004")]
    public readonly InputSlot<Vector3> WallSize = new();

    [Input(Guid = "d13d7e22-04f4-4d03-8af6-000000001005")]
    public readonly InputSlot<Vector3> WallCenter = new();

    [Input(Guid = "d13d7e22-04f4-4d03-8af6-000000001006")]
    public readonly InputSlot<bool> PairDepthModes = new();

    internal enum DrawMode { Standard, Indirect, IndirectLod, IndirectDepth, IndirectLodAndDepth }
}
