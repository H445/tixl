namespace Examples.Lib.render.culling;

[Guid("b9fbad68-bf5a-5782-a862-5f64a6a45920")]
internal sealed class CullMeshExample : Instance<CullMeshExample>
{
    [Output(Guid = "4c5bf29a-39f9-55d9-9f82-ca52f64a525e")]
    public readonly Slot<Texture2D> Image = new();
}
