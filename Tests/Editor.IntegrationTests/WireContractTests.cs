using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Collection("OwnedContracts")]
[Trait("Category", "OwnedEditor")]
[Trait("ContractGroup", "Wire")]
public sealed class WireContractTests(ITestOutputHelper output)
{
    [Fact]
    public void MalformedLines_ReturnErrorsAndPreserveConnection()
    {
        using var editor = Start();
        using var wire = new OwnedWireConnection(editor.OwnedSession!);
        wire.SendText("\n  \n{invalid}\n[]\n");
        for (var i = 0; i < 2; i++)
        {
            var reply = wire.Read();
            AssertError(reply, "PARSE_ERROR");
            Assert.Equal(JTokenType.Null, reply["id"]!.Type);
        }
        wire.Send(new JObject { ["id"] = "missing" },
                  new JObject { ["id"] = "null", ["method"] = null },
                  new JObject { ["id"] = "unknown", ["method"] = "Ping" },
                  new JObject { ["id"] = "numeric", ["method"] = 123 },
                  new JObject { ["id"] = "recovered", ["method"] = "ping" });
        AssertError(wire.Read(), "MISSING_METHOD", "missing");
        AssertError(wire.Read(), "MISSING_METHOD", "null");
        AssertError(wire.Read(), "UNKNOWN_METHOD", "unknown");
        AssertError(wire.Read(), "UNKNOWN_METHOD", "numeric");
        var recovered = wire.Read();
        AssertEnvelope(recovered);
        Assert.True(recovered["ok"]!.Value<bool>());
        Assert.Equal("recovered", recovered["id"]!.Value<string>());
    }

    [Fact]
    public void Ids_AreEchoedWithoutUniquenessOrTypeEnforcement()
    {
        using var editor = Start();
        using var wire = new OwnedWireConnection(editor.OwnedSession!);
        foreach (var id in new JToken[] { JValue.CreateNull(), new JValue(42), new JValue("ü"), new JArray(1, "x"), new JObject { ["nested"] = true } })
        {
            wire.Send(new JObject { ["id"] = id, ["method"] = "ping" });
            var reply = wire.Read();
            AssertEnvelope(reply);
            Assert.True(reply["ok"]!.Value<bool>());
            Assert.True(JToken.DeepEquals(id, reply["id"]));
        }
        wire.Send(new JObject { ["method"] = "ping" });
        Assert.Equal(JTokenType.Null, wire.Read()["id"]!.Type);
        wire.Send(new JObject { ["id"] = "duplicate", ["method"] = "ping" },
                  new JObject { ["id"] = "duplicate", ["method"] = "ping" });
        Assert.Equal("duplicate", wire.Read()["id"]!.Value<string>());
        Assert.Equal("duplicate", wire.Read()["id"]!.Value<string>());
    }

    [Fact]
    public void CompetingClients_ReceiveOnlyTheirOwnReplies()
    {
        using var editor = Start();
        using var first = new OwnedWireConnection(editor.OwnedSession!);
        using var second = new OwnedWireConnection(editor.OwnedSession!);
        first.Send(new JObject { ["id"] = "shared", ["method"] = "getPaths" });
        second.Send(new JObject { ["id"] = "shared", ["method"] = "getVersion" });
        var firstReply = first.Read();
        var secondReply = second.Read();
        Assert.Equal("shared", firstReply["id"]!.Value<string>());
        Assert.Equal("shared", secondReply["id"]!.Value<string>());
        editor.OwnedSession!.Paths.Verify(Assert.IsType<JObject>(firstReply["result"]));
        Assert.Equal(1, secondReply["result"]!["protocolVersion"]!.Value<int>());
        Assert.Null(secondReply["result"]!["settings"]);
        Assert.True(editor.Client.Ping().Ok);
    }

    [Fact]
    public void Disconnect_DoesNotCancelQueuedMutations()
    {
        using var editor = Start();
        editor.Client.Call("setPlayback", new { speed = 0 }).Require("pause");
        editor.Client.Call("setTime", new { timeInBars = 0 }).Require("reset");
        using (var wire = new OwnedWireConnection(editor.OwnedSession!))
        {
            wire.Send(new JObject { ["id"] = "stall", ["method"] = "stallMainThread", ["seconds"] = 0.5 },
                      new JObject { ["id"] = "queued", ["method"] = "setTime", ["timeInBars"] = 7 });
        }
        var timer = Stopwatch.StartNew();
        double bars;
        do
        {
            editor.Client.PumpFrames(1);
            bars = editor.Client.Call("getContext").Require("observe disconnected mutation")["time"]!["timeInBars"]!.Value<double>();
        }
        while (bars != 7 && timer.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(7, bars);
        using var reconnected = new OwnedWireConnection(editor.OwnedSession!);
        reconnected.Send(new JObject { ["id"] = "new-connection", ["method"] = "ping" });
        Assert.Equal("new-connection", reconnected.Read()["id"]!.Value<string>());
    }

    private EditorFixture Start()
    {
        var editor = OwnedEditorFixture.StartEditor(createProject: false);
        output.WriteLine("Evidence: " + editor.OwnedSession!.EvidenceDirectory);
        return editor;
    }

    private static void AssertError(JObject reply, string code, string? id = null)
    {
        AssertEnvelope(reply);
        Assert.False(reply["ok"]!.Value<bool>());
        Assert.Equal(code, reply["error"]!["code"]!.Value<string>());
        Assert.NotEmpty(reply["error"]!["detail"]!.Value<string>()!);
        Assert.Null(reply["result"]);
        if (id != null)
            Assert.Equal(id, reply["id"]!.Value<string>());
    }

    private static void AssertEnvelope(JObject reply)
    {
        Assert.Equal(JTokenType.Boolean, reply["ok"]!.Type);
        Assert.True(reply.ContainsKey("id"));
        foreach (var field in new[] { "frame", "playbackFrame", "structureVersion" })
            Assert.Equal(JTokenType.Integer, reply[field]!.Type);
    }
}
