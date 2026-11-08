using Newtonsoft.Json.Linq;
using TiXL.DebugClient;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Collection("OwnedContracts")]
[Trait("Category", "OwnedEditor")]
[Trait("ContractGroup", "Output")]
public sealed class OutputContractTests(ITestOutputHelper output)
{
    [Fact]
    public void PrimitiveOutputs_PreserveValuesAndTriggerFlanks()
    {
        using var editor = Start();
        var client = editor.Client;
        var integer = client.AddOp("AddInt");
        client.SetInput(integer, "Input1", new JValue(-123));
        client.SetInput(integer, "Input2", new JValue(17));
        Assert.Equal(-106, Read(client, integer)["value"]!.Value<int>());
        Assert.Equal("Int32", Read(client, integer)["valueType"]!.Value<string>());
        Assert.Equal("INVALID_PARAM", client.Call("setInput", new JObject { ["childId"] = integer, ["inputName"] = "Input1", ["value"] = null }).ErrorCode);
        var vector = client.AddOp("AddVec2");
        client.SetInput(vector, "Input1", JObject.FromObject(new { X = 1.25, Y = -2.5 }));
        client.SetInput(vector, "Input2", JObject.FromObject(new { X = 3.75, Y = 0.5 }));
        Assert.Equal(new[] { 5.0, -2.0 }, Read(client, vector)["value"]!.Values<double>());
        var color = client.AddOp("RgbaToColor");
        foreach (var (name, value) in new[] { ("R", 0.125), ("G", 0.25), ("B", 0.5), ("A", 1.0) })
            client.SetInput(color, name, new JValue(value));
        Assert.Equal(new[] { 0.125, 0.25, 0.5, 1.0 }, Read(client, color)["value"]!.Values<double>());
        var flip = client.AddOp("FlipBool");
        client.SetInput(flip, "DefaultValue", new JValue(false));
        client.SetInput(flip, "ResetTrigger", new JValue(true));
        Assert.False(Read(client, flip)["value"]!.Value<bool>());
        client.SetInput(flip, "ResetTrigger", new JValue(false));
        client.SetInput(flip, "Trigger", new JValue(true));
        Assert.True(Read(client, flip)["value"]!.Value<bool>());
        Assert.True(Read(client, flip)["value"]!.Value<bool>());
        client.SetInput(flip, "Trigger", new JValue(false));
        Assert.True(Read(client, flip)["value"]!.Value<bool>());
        client.SetInput(flip, "Trigger", new JValue(true));
        Assert.False(Read(client, flip)["value"]!.Value<bool>());
    }

    [Fact]
    public void ListAndMatrixInputs_AreInspectableWhileTheirOutputsRemainOpaque()
    {
        using var editor = Start();
        var client = editor.Client;
        var list = client.AddOp("IntList");
        client.SetInput(list, "List", new JObject { ["Values"] = new JArray(1, -2, 3) });
        var result = Read(client, list);
        Assert.Equal("List`1", result["valueType"]!.Value<string>());
        Assert.Equal(JTokenType.Null, result["value"]!.Type);
        Assert.Equal(new[] { 1, -2, 3 }, Input(client, list, "List")["Values"]!.Values<int>());
        var matrix = client.AddOp("MulMatrix");
        var values = new JArray(2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 4, 0, 5, 6, 7, 1);
        client.SetInput(matrix, "MatrixA", values);
        Assert.Equal(values.Values<double>(), Input(client, matrix, "MatrixA").Values<double>());
        result = Read(client, matrix);
        Assert.Equal("Matrix4x4", result["valueType"]!.Value<string>());
        Assert.Equal(JTokenType.Null, result["value"]!.Type);
        client.SetInput(matrix, "MatrixA", new JArray(1, 2));
        Assert.Equal(new[] { 1.0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }, Input(client, matrix, "MatrixA").Values<double>());
    }

    [Fact]
    public void StructuredPoints_CpuAndGpuReadbackAgreeAndRespectRanges()
    {
        using var editor = Start();
        var client = editor.Client;
        var points = client.AddOp("_PointsForLine");
        Assert.Equal(JTokenType.Null, client.Call("getOutput", new { childId = points }).Require("unevaluated list")["value"]!.Type);
        client.SetInput(points, "From", JObject.FromObject(new { X = 1, Y = 2, Z = 3 }));
        client.SetInput(points, "To", JObject.FromObject(new { X = 4, Y = 5, Z = 6 }));
        client.SetInput(points, "W", new JValue(0.25));
        client.SetInput(points, "WOffset", new JValue(0.5));
        client.SetInput(points, "AddSeparator", new JValue(false));
        var cpu = Read(client, points, readCount: 99)["value"]!;
        Assert.Equal("Point", cpu["type"]!.Value<string>());
        Assert.Equal(2, cpu["elementCount"]!.Value<int>());
        Assert.Equal(64, cpu["elementSizeInBytes"]!.Value<int>());
        var elements = Assert.IsType<JArray>(cpu["elements"]);
        Assert.Equal(2, elements.Count);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, elements[0]["position"]!.Values<double>());
        Assert.Equal(new[] { 4.0, 5.0, 6.0 }, elements[1]["position"]!.Values<double>());
        Assert.Equal(0.25, elements[0]["f1"]!.Value<double>());
        Assert.Equal(0.75, elements[1]["f1"]!.Value<double>());
        var buffer = client.AddOp("ListToBuffer");
        Assert.Equal(JTokenType.Null, Read(client, buffer)["value"]!.Type);
        client.ConnectOps(points, "ResultList", buffer, "Lists");
        var gpu = Read(client, buffer, readCount: 99)["value"]!;
        Assert.Equal(128, gpu["sizeInBytes"]!.Value<int>());
        Assert.Equal(64, gpu["stride"]!.Value<int>());
        Assert.Equal(2, gpu["elementCount"]!.Value<int>());
        Assert.False(gpu["disposed"]!.Value<bool>());
        Assert.True(JToken.DeepEquals(elements, gpu["elements"]));
        foreach (var child in new[] { points, buffer })
        {
            Assert.Null(Read(client, child)["value"]!["elements"]);
            Assert.Null(Read(client, child, readCount: -1)["value"]!["elements"]);
            Assert.Empty(Assert.IsType<JArray>(Read(client, child, readCount: 10, readOffset: 999)["value"]!["elements"]));
            var last = Assert.Single(Read(client, child, readCount: 10, readOffset: 1)["value"]!["elements"]!);
            Assert.Equal(1, last["index"]!.Value<int>());
            Assert.True(JToken.DeepEquals(elements, Read(client, child, readCount: 99, readOffset: -10)["value"]!["elements"]));
        }
    }

    [Fact]
    public void IntegerBuffer_ReadbackReinterpretsBytesAsFloats()
    {
        using var editor = Start();
        var client = editor.Client;
        var buffer = client.AddOp("IntListToBuffer");
        client.SetInput(buffer, "IntList", new JObject { ["Values"] = new JArray(1065353216, -1073741824) });
        var value = Read(client, buffer, readCount: 2)["value"]!;
        Assert.Equal(4, value["stride"]!.Value<int>());
        Assert.Equal(8, value["sizeInBytes"]!.Value<int>());
        var elements = Assert.IsType<JArray>(value["elements"]);
        Assert.Equal(2, elements.Count);
        Assert.Equal(1.0, Assert.Single(elements[0]).Value<double>());
        Assert.Equal(-2.0, Assert.Single(elements[1]).Value<double>());
        client.SetInput(buffer, "IntList", new JObject { ["Values"] = new JArray() });
        Assert.Equal(JTokenType.Null, Read(client, buffer)["value"]!.Type);
    }

    [Fact]
    public void MultiInputConnections_PreserveOrderThroughDeleteAndUndo()
    {
        using var editor = Start();
        var client = editor.Client;
        var first = client.AddOp("AddInt");
        var second = client.AddOp("AddInt");
        client.SetInput(first, "Input1", new JValue(1065353216));
        client.SetInput(second, "Input1", new JValue(1073741824));
        var list = client.AddOp("IntsToList");
        var buffer = client.AddOp("IntListToBuffer");
        client.Call("connect", new { sourceChildId = first, targetChildId = list, multiInputIndex = 0 }).Require("first list input");
        client.Call("connect", new { sourceChildId = second, targetChildId = list, multiInputIndex = 0 }).Require("insert before first");
        client.ConnectOps(list, "Result", buffer, "IntList");
        Assert.Equal(new[] { 2.0, 1.0 }, Read(client, buffer, readCount: 99)["value"]!["elements"]!.Select(e => Assert.Single(e).Value<double>()));
        client.DeleteOp(second);
        Assert.Equal(new[] { 1.0 }, Read(client, buffer, readCount: 99)["value"]!["elements"]!.Select(e => Assert.Single(e).Value<double>()));
        client.Undo();
        Assert.Equal(new[] { 2.0, 1.0 }, Read(client, buffer, readCount: 99)["value"]!["elements"]!.Select(e => Assert.Single(e).Value<double>()));
    }

    [Fact]
    public void GeometrySummaryAndObjDump_AgreeWithKnownCube()
    {
        using var editor = Start();
        var client = editor.Client;
        var cube = client.AddOp("CubeGeometry");
        client.SetInput(cube, "Size", JObject.FromObject(new { X = 2, Y = 4, Z = 6 }));
        var path = Path.Combine(editor.OwnedSession!.Paths["temp"], "cube.obj");
        var value = client.Call("getOutput", new { childId = cube, update = true, dumpObj = path }).Require("geometry summary and dump")["value"]!;
        Assert.Equal(8, value["pointCount"]!.Value<int>());
        Assert.Equal(6, value["faceCount"]!.Value<int>());
        Assert.Equal(24, value["cornerCount"]!.Value<int>());
        Assert.Equal(new[] { -1.0, -2, -3 }, value["boundsMin"]!.Values<double>());
        Assert.Equal(new[] { 1.0, 2, 3 }, value["boundsMax"]!.Values<double>());
        Assert.Equal(48, value["volume"]!.Value<double>(), 6);
        Assert.Equal(0, value["boundaryEdges"]!.Value<int>());
        Assert.Equal(0, value["nonManifoldEdges"]!.Value<int>());
        var lines = File.ReadAllLines(path);
        Assert.Equal(8, lines.Count(line => line.StartsWith("v ")));
        Assert.Equal(6, lines.Count(line => line.StartsWith("f ")));
        Assert.All(lines.Where(line => line.StartsWith("f ")), line => Assert.Equal(5, line.Split(' ').Length));
        File.Copy(path, Path.Combine(editor.OwnedSession.EvidenceDirectory, "cube.obj"));
        Assert.Equal("INTERNAL_ERROR", client.Call("getOutput", new { childId = cube, dumpObj = Path.Combine(path, "missing.obj") }).ErrorCode);
    }

    private EditorFixture Start()
    {
        var editor = OwnedEditorFixture.StartEditor();
        output.WriteLine("Evidence: " + editor.OwnedSession!.EvidenceDirectory);
        return editor;
    }

    private static JObject Read(DebugProtocolClient client, Guid child, int readCount = 0, int readOffset = 0)
        => client.Call("getOutput", new { childId = child, update = true, readCount, readOffset }).Require("output value");

    private static JToken Input(DebugProtocolClient client, Guid child, string name)
    {
        var node = Assert.Single(client.GetGraphState(includeDefaults: true)["children"]!, c => c["childId"]!.Value<string>() == child.ToString());
        return Assert.Single(node["inputs"]!, input => input["name"]!.Value<string>() == name)["value"]!;
    }
}
