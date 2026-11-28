namespace Examples.Lib.mesh;

[Guid("d13d7e22-04f4-4d03-8af6-000000001100")]
internal sealed class _IndirectOrbitScene : Instance<_IndirectOrbitScene>
{
    [Output(Guid = "d13d7e22-04f4-4d03-8af6-000000001101")]
    public readonly Slot<Geometry> Fine = new();

    [Output(Guid = "d13d7e22-04f4-4d03-8af6-000000001102")]
    public readonly Slot<Geometry> Coarse = new();

    [Output(Guid = "d13d7e22-04f4-4d03-8af6-000000001103", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Vector3> Eye = new();

    [Output(Guid = "d13d7e22-04f4-4d03-8af6-000000001104", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<int> PathIndex = new();

    [Output(Guid = "d13d7e22-04f4-4d03-8af6-000000001105", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> LodPixels = new();

    [Output(Guid = "d13d7e22-04f4-4d03-8af6-000000001106", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<bool> UseDepth = new();

    [Input(Guid = "d13d7e22-04f4-4d03-8af6-000000001107")]
    public readonly InputSlot<int> Mode = new();


    internal const string SampleVariable = "DrawGeometryIndirectExample.Sample";
    internal const int Steps = 120;

    public _IndirectOrbitScene()
    {
        Fine.UpdateAction = _ => Fine.Value ??= MakeField(24, 12);
        Coarse.UpdateAction = _ => Coarse.Value ??= MakeField(8, 4);
        Eye.UpdateAction = PathIndex.UpdateAction = LodPixels.UpdateAction = UseDepth.UpdateAction = UpdateView;
    }

    private void UpdateView(EvaluationContext context)
    {
        var mode = Mode.GetValue(context);
        var turns = context.LocalTime / 8;
        if (context.FloatVariables.TryGetValue(SampleVariable, out var sample))
        {
            mode = (int)sample / Steps;
            turns = ((int)sample % Steps) / (double)Steps;
        }
        var angle = (float)(turns * Math.Tau);
        Eye.Value = new Vector3(22 * MathF.Sin(angle), 6 - 3 * MathF.Cos(angle), 22 * MathF.Cos(angle));
        PathIndex.Value = mode == 0 ? 0 : 1;
        LodPixels.Value = mode is 2 or 4 ? 100 : 0;
        UseDepth.Value = mode is 3 or 4;
        Eye.DirtyFlag.Clear();
        PathIndex.DirtyFlag.Clear();
        LodPixels.DirtyFlag.Clear();
        UseDepth.DirtyFlag.Clear();
    }

    // Both resolutions use the same centers, colors and part order.
    private static Geometry MakeField(int around, int tube)
    {
        const int side = 15;
        var perPart = around * tube;
        var count = side * side * perPart;
        var g = new Geometry();
        g.Resize(count, count, count * 4);
        g.Parts = new PrimitivePart[side * side];
        var colors = g.Attributes.GetOrCreate<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Point, count).Values;
        for (var part = 0; part < g.Parts.Length; part++)
        {
            int x = part % side - side / 2, z = part / side - side / 2;
            var center = new Vector3(2.1f * x, .6f * MathF.Cos(x + z), 2.1f * z);
            var start = part * perPart;
            g.Parts[part] = new PrimitivePart(start, perPart, center, part, part);
            var hue = (x + 7) / 14f;
            var color = Vector4.Lerp(new Vector4(.04f, .62f, .87f, 1), new Vector4(.94f, .32f, .09f, 1), hue);
            for (var u = 0; u < around; u++)
            for (var v = 0; v < tube; v++)
            {
                var major = MathF.Tau * u / around;
                var minor = MathF.Tau * v / tube;
                var ring = .64f + .23f * MathF.Cos(minor);
                var i = start + u * tube + v;
                g.Positions[i] = center + new Vector3(ring * MathF.Cos(major), .23f * MathF.Sin(minor), ring * MathF.Sin(major));
                colors[i] = color * (.72f + .28f * MathF.Sin(minor));
                colors[i].W = 1;
                var nextU = (u + 1) % around;
                var nextV = (v + 1) % tube;
                g.VertexPoints[4 * i] = i;
                g.VertexPoints[4 * i + 1] = start + u * tube + nextV;
                g.VertexPoints[4 * i + 2] = start + nextU * tube + nextV;
                g.VertexPoints[4 * i + 3] = start + nextU * tube + v;
                g.PrimitiveVertexOffsets[i] = 4 * i;
                g.PrimitiveClosed[i] = true;
            }
        }
        g.PrimitiveVertexOffsets[count] = 4 * count;
        return g;
    }
}
