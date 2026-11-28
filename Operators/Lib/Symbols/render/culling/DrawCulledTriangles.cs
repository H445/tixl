using T3.Core.Utils;

namespace Lib.render.culling;

[Guid("f435a3e2-9d1d-55c7-b3a8-006cbe4f0f16")]
internal sealed class DrawCulledTriangles : Instance<DrawCulledTriangles>
{
    [Output(Guid = "9203bed2-a397-5217-81cc-976285a26834")]
    public readonly Slot<Command> Draw = new();

    private enum ShadingModes
    {
        Default = 0,
        Flat = 1,
    }

    [Input(Guid = "fc2f9242-4c2c-4df3-bda2-f3b8d9638806")] public readonly InputSlot<bool> OpaquePrepass = new();

    [Input(Guid = "5eaa034a-fe2f-5836-978b-8052078ece41")]
    public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new();

    [Input(Guid = "5eb9f991-4b3e-5d9c-8081-af046eef0d26")]
    public readonly InputSlot<System.Numerics.Vector4> Color = new();

    [Input(Guid = "fb00642d-7aeb-5a0f-85d2-58e3abf89205")]
    public readonly InputSlot<float> AlphaCutOff = new();

    [Input(Guid = "0b2ab808-5666-52d5-b80f-6cc5032e176b", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "c08454db-64fd-5e30-a6b2-6d8981dc3be4", MappedType = typeof(FillMode))]
    public readonly InputSlot<int> FillMode = new();

    [Input(Guid = "a3b701c9-5107-535b-a397-c9c1be4e1401")]
    public readonly InputSlot<T3.Graphics.Compat.CullMode> Culling = new();

    [Input(Guid = "eb6969c1-d8eb-5672-9e20-66f68e475e3b", MappedType = typeof(ShadingModes))]
    public readonly InputSlot<int> Shading = new();

    [Input(Guid = "ddd65751-ccd5-5ccb-ab0a-99cc0c57da0f")]
    public readonly InputSlot<float> SpecularAA = new();

    [Input(Guid = "614e7f3b-2003-5705-ba0c-ae2cceea05f4")]
    public readonly InputSlot<bool> EnableZTest = new();

    [Input(Guid = "d2927e15-e9d6-5eb3-b5ac-095d95113c5e")]
    public readonly InputSlot<bool> EnableZWrite = new();

    [Input(Guid = "d37aca8e-d24f-5767-8fc6-06b240c69e5f")]
    public readonly InputSlot<T3.Graphics.Compat.Filter> Filter = new();

    [Input(Guid = "33b3bd12-e4fd-52d0-96ca-7371407624c7")]
    public readonly InputSlot<T3.Graphics.Compat.TextureAddressMode> WrapMode = new();


    [Input(Guid = "e3b16858-ed02-5f46-a03a-9c4bddd212d7")]
    public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> FragmentField = new();

    [Input(Guid = "bdc908d0-c516-5a2e-91b5-1b05e7174447")]
    public readonly InputSlot<string> ShaderDefines = new();
    [Input(Guid = "b9142068-e4a8-558b-a3cb-8b2583dfba5f")] public readonly InputSlot<bool> BypassCulling = new();
    [Input(Guid = "a5be3978-7ef0-53f4-a20e-298c11ffadf3")] public readonly InputSlot<bool> CacheStaticMesh = new();
}
