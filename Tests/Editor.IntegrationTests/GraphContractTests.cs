using Newtonsoft.Json.Linq;
using TiXL.DebugClient;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Trait("Category", "OwnedEditor")]
[Trait("ContractGroup", "Graph")]
[Collection("OwnedContracts")]
public sealed class GraphContractTests(ITestOutputHelper output)
{
    [Fact]
    public void GraphState_ReportsDefaultsKnownSymbolAndInvalidComposition()
    {
        using var editor = Start();
        var client = editor.Client;
        var child = Add(client, AddSymbol);
        var sparse = Child(client.GetGraphState(), child);
        Assert.Empty((JArray)sparse["inputs"]!);
        var detailed = Child(client.GetGraphState(includeDefaults: true), child);
        Assert.Equal(2, detailed["inputs"]!.Count());
        Assert.All(detailed["inputs"]!, input => Assert.True(input["isDefault"]!.Value<bool>()));
        var definition = client.Call("getGraphState", new { compositionId = AddSymbol }).Require("known symbol");
        Assert.Equal(AddSymbol, definition["symbolId"]!.Value<string>());
        Assert.Empty((JArray)definition["children"]!);
        Assert.Equal("INVALID_PARAM", client.Call("getGraphState", new { compositionId = "invalid" }).ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("getGraphState", new { compositionId = Guid.NewGuid() }).ErrorCode);
    }

    [Fact]
    public void ScalarInputs_SelectorsErrorsAndUndo()
    {
        using var editor = Start();
        var client = editor.Client;
        var child = Add(client, AddSymbol);
        client.Call("setInput", new { childId = child, inputName = "iNpUt1", value = 2.5 }).Require("case-insensitive name");
        client.Call("setInput", new { childId = child, inputId = AddInput2, inputName = "Input1", value = 4.0 }).Require("GUID precedence");
        Assert.Equal(6.5, Value(client, child).Value<double>());
        client.Call("setInput", new { childId = child, inputId = "invalid", inputName = "Input2", value = -3.0 }).Require("malformed GUID falls back to name");
        Assert.Equal(-0.5, Value(client, child).Value<double>());
        Assert.Equal("NOT_FOUND", client.Call("setInput", new { childId = child, inputId = Guid.NewGuid(), inputName = "Input1", value = 1 }).ErrorCode);
        Assert.Equal("MISSING_PARAM", client.Call("setInput", new { childId = child, inputName = "Input1" }).ErrorCode);
        Assert.Equal("INVALID_PARAM", client.Call("setInput", new { childId = child, inputName = "Input1", value = "not-a-float" }).ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("setInput", new { childId = Guid.NewGuid(), inputName = "Input1", value = 1 }).ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("setInput", new { compositionId = "invalid", childId = child, inputName = "Input1", value = 1 }).ErrorCode);
        client.Undo();
        Assert.Equal(6.5, Value(client, child).Value<double>());
        client.Call("redo").Require("redo");
        Assert.Equal(-0.5, Value(client, child).Value<double>());
        var inputs = Child(client.GetGraphState(), child)["inputs"]!;
        Assert.Equal(2, inputs.Count());
        Assert.All(inputs, input => Assert.False(input["isDefault"]!.Value<bool>()));
    }

    [Fact]
    public void VectorOutput_DefaultUsesCacheAndExplicitUpdateEvaluates()
    {
        using var editor = Start();
        var client = editor.Client;
        var child = Add(client, VectorSymbol);
        client.SetInput(child, "Input1", JObject.FromObject(new { X = 1.0, Y = 2.0, Z = 3.0 }));
        client.SetInput(child, "Input2", JObject.FromObject(new { X = 10.0, Y = 20.0, Z = 30.0 }));
        Assert.Equal(new[] { 11.0, 22.0, 33.0 }, Value(client, child).Values<double>());
        client.SetInput(child, "Input1", JObject.FromObject(new { X = -1.0, Y = -2.0, Z = -3.0 }));
        var cached = client.Call("getOutput", new { childId = child }).Require("cached output");
        Assert.Equal("Vector3", cached["valueType"]!.Value<string>());
        Assert.Equal(new[] { 11.0, 22.0, 33.0 }, cached["value"]!.Values<double>());
        Assert.Equal(new[] { 9.0, 18.0, 27.0 }, Value(client, child).Values<double>());
    }

    [Fact]
    public void StringCodec_RoundTrips()
    {
        using var editor = Start();
        var child = Add(editor.Client, StringSymbol);
        foreach (var text in new[] { "", "Quotes: \"hello\"\nUnicode: café 日本語" })
        {
            editor.Client.SetInput(child, "InputString", new JValue(text));
            Assert.Equal(text, Value(editor.Client, child).Value<string>());
        }
    }

    [Fact]
    public void OutputSelectors_UnknownAndMalformedChildBehavior()
    {
        using var editor = Start();
        var client = editor.Client;
        var child = Add(client, AddSymbol);
        var named = client.Call("getOutput", new { childId = child, outputName = "rEsUlT" }).Require("output name");
        Assert.Equal(AddOutput, named["outputId"]!.Value<string>());
        var byId = client.Call("getOutput", new { childId = child, outputId = AddOutput, outputName = "missing" }).Require("output ID precedence");
        Assert.Equal(AddOutput, byId["outputId"]!.Value<string>());
        Assert.Equal("NOT_FOUND", client.Call("getOutput", new { childId = child, outputId = Guid.NewGuid(), outputName = "Result" }).ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("getOutput", new { childId = Guid.NewGuid() }).ErrorCode);
        var root = client.Call("getOutput");
        var malformed = client.Call("getOutput", new { childId = "invalid" });
        Assert.Equal(root.Ok, malformed.Ok);
        Assert.Equal(root.ErrorCode, malformed.ErrorCode);
        if (root.Ok)
        {
            Assert.Equal(root.Result!["outputId"], malformed.Result!["outputId"]);
            Assert.Equal(root.Result["valueType"], malformed.Result["valueType"]);
        }
    }

    [Fact]
    public void UnknownSymbolGuid_CharacterizesExistingFalseSuccess()
    {
        using var editor = Start();
        var client = editor.Client;
        var before = client.GetGraphState()["children"]!.Count();
        var reply = client.Call("addOp", new { symbolId = Guid.NewGuid(), symbolName = "Add" }).Require("unknown GUID current behavior");
        Assert.True(Guid.TryParse(reply["childId"]!.Value<string>(), out var child));
        var after = client.GetGraphState()["children"]!;
        Assert.Equal(before, after.Count());
        Assert.DoesNotContain(after, c => c["childId"]!.Value<string>() == child.ToString());
        Assert.Equal("NOT_FOUND", client.Call("addOp", new { symbolName = "DefinitelyNoSuchOperator" }).ErrorCode);
        Assert.Equal("MISSING_PARAM", client.Call("addOp").ErrorCode);
    }

    [Fact]
    public void Connections_ResolveSelectorsRejectMismatchAndCycle()
    {
        using var editor = Start();
        var client = editor.Client;
        var first = Add(client, AddSymbol);
        var second = Add(client, AddSymbol);
        var text = Add(client, StringSymbol);
        client.SetInput(first, "Input1", new JValue(7.0));
        client.Call("connect", new { sourceChildId = first, sourceOutput = AddOutput, targetChildId = second, targetInput = "iNpUt1" }).Require("connect");
        Assert.Equal(7.0, Value(client, second).Value<double>());
        Assert.Contains(client.GetGraphState()["connections"]!, c => c["sourceParentOrChildId"]!.Value<string>() == first.ToString()
                                                                 && c["targetParentOrChildId"]!.Value<string>() == second.ToString());
        Assert.Equal("CYCLE", client.Call("connect", new { sourceChildId = second, targetChildId = first }).ErrorCode);
        Assert.Equal("TYPE_MISMATCH", client.Call("connect", new { sourceChildId = first, targetChildId = text }).ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("connect", new { sourceChildId = first, sourceOutput = "missing", targetChildId = second }).ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("connect", new { sourceChildId = Guid.NewGuid(), targetChildId = second }).ErrorCode);
        client.DeleteOp(first);
        Assert.DoesNotContain(client.GetGraphState()["connections"]!, c => c["sourceParentOrChildId"]!.Value<string>() == first.ToString());
        client.Undo();
        Assert.Equal(7.0, Value(client, second).Value<double>());
    }

    [Fact]
    public void SelectionBypassDeleteAndHistory_RoundTrip()
    {
        using var editor = Start();
        var client = editor.Client;
        var child = Add(client, AddSymbol);
        client.SetInput(child, "Input1", new JValue(2.0));
        client.SetInput(child, "Input2", new JValue(3.0));
        Assert.Equal(5.0, Value(client, child).Value<double>());
        Assert.False(client.Call("setBypass", new { childId = child }).Require("unconnected bypass is a no-op")["bypassed"]!.Value<bool>());
        var upstream = Add(client, AddSymbol);
        client.SetInput(upstream, "Input1", new JValue(2.0));
        var consumer = Add(client, AddSymbol);
        client.ConnectOps(upstream, "Result", child, "Input1");
        client.ConnectOps(child, "Result", consumer, "Input1");
        Assert.True(client.Call("setBypass", new { childId = child }).Require("bypass default")["bypassed"]!.Value<bool>());
        Assert.True(Child(client.GetGraphState(), child)["isBypassed"]!.Value<bool>());
        Assert.Equal(2.0, Value(client, child).Value<double>());
        client.Undo();
        Assert.Equal(5.0, Value(client, child).Value<double>());
        Assert.False(Child(client.GetGraphState(), child).ContainsKey("isBypassed"));
        var missing = Guid.NewGuid();
        var selection = client.Call("select", new { childIds = new[] { child, missing } }).Require("partial selection");
        Assert.Equal(1, selection["selectedCount"]!.Value<int>());
        Assert.Equal(missing.ToString(), Assert.Single((JArray)selection["notFound"]!).Value<string>());
        Assert.Equal("INVALID_PARAM", client.Call("select", new { childIds = new[] { "invalid" } }).ErrorCode);
        Assert.Equal("MISSING_PARAM", client.Call("select", new { add = true }).ErrorCode);
        Assert.Equal(0, client.Call("select", new { childId = "invalid" }).Require("malformed single ID clears")["selectedCount"]!.Value<int>());
        client.Select(child);
        Assert.True(client.Call("deleteSelection", new { disconnect = true }).Require("delete selection")["deleted"]!.Value<bool>());
        Assert.DoesNotContain(client.GetGraphState()["children"]!, c => c["childId"]!.Value<string>() == child.ToString());
        var undone = client.Call("undo").Require("undo");
        Assert.True(undone["canRedo"]!.Value<bool>());
        Assert.Equal(child.ToString(), Child(client.GetGraphState(), child)["childId"]!.Value<string>());
        client.Call("redo").Require("redo");
        Assert.DoesNotContain(client.GetGraphState()["children"]!, c => c["childId"]!.Value<string>() == child.ToString());
        Assert.Equal("NOT_FOUND", client.Call("deleteOp", new { childId = child }).ErrorCode);
    }

    [Fact]
    public void Bypass_UnconnectedInputCanPartiallyApplyDespiteFalseState()
    {
        using var editor = Start();
        var client = editor.Client;
        var child = Add(client, AddSymbol);
        var consumer = Add(client, AddSymbol);
        client.SetInput(child, "Input1", new JValue(2.0));
        client.SetInput(child, "Input2", new JValue(3.0));
        client.ConnectOps(child, "Result", consumer, "Input1");
        Assert.Equal(5.0, Value(client, consumer).Value<double>());
        var cursor = client.GetLatestLogSeq();
        Assert.False(client.Call("setBypass", new { childId = child }).Require("partial bypass current behavior")["bypassed"]!.Value<bool>());
        Assert.False(Child(client.GetGraphState(), child).ContainsKey("isBypassed"));
        Assert.Equal(2.0, Value(client, consumer).Value<double>());
        Assert.Contains(client.GetLogTail(cursor, "error"), e => e["message"]!.Value<string>()!.Contains("Failed to set bypass state"));
    }

    private EditorFixture Start()
    {
        var editor = OwnedEditorFixture.StartEditor();
        output.WriteLine("Evidence: " + editor.OwnedSession!.EvidenceDirectory);
        return editor;
    }

    private static Guid Add(DebugProtocolClient client, string symbolId)
        => Guid.Parse(client.Call("addOp", new { symbolId }).Require("addOp")["childId"]!.Value<string>()!);

    private static JObject Child(JObject graph, Guid id)
        => (JObject)Assert.Single(graph["children"]!, c => c["childId"]!.Value<string>() == id.ToString());

    private static JToken Value(DebugProtocolClient client, Guid child)
        => client.Call("getOutput", new { childId = child, update = true }).Require("evaluate output")["value"]!;

    private const string AddSymbol = "c160f925-0a66-4505-a569-cadd878dbb6f";
    private const string AddOutput = "5ce9c625-f890-4620-9747-c98eab4b9447";
    private const string AddInput2 = "993d59bb-1fc0-4857-a36d-629b0e7aa0d2";
    private const string VectorSymbol = "15ac7153-69af-45f8-bcdd-50cdef0c9ae1";
    private const string StringSymbol = "5880cbc3-a541-4484-a06a-0e6f77cdbe8e";
}
