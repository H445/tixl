using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Collection("OwnedContracts")]
[Trait("Category", "OwnedEditor")]
[Trait("ContractGroup", "Playback")]
public sealed class PlaybackContractTests(ITestOutputHelper output)
{
    [Fact]
    public void Time_UsesBarsPrecedenceAndCurrentTempo()
    {
        using var editor = Start();
        var client = editor.Client;
        client.Call("setPlayback", new { speed = 0 }).Require("pause");
        var bpm = client.Call("getContext").Require("context")["time"]!["bpm"]!.Value<double>();
        var time = client.Call("setTime", new { timeInBars = 1.25, timeInSecs = 99 }).Require("bars precedence");
        Assert.Equal(1.25, time["timeInBars"]!.Value<double>(), 8);
        Assert.Equal(1.25 * 240 / bpm, time["timeInSecs"]!.Value<double>(), 8);
        time = client.Call("setTime", new { timeInSecs = -2.5 }).Require("negative seconds");
        Assert.Equal(-2.5, time["timeInSecs"]!.Value<double>(), 8);
        Assert.Equal(-2.5 * bpm / 240, time["timeInBars"]!.Value<double>(), 8);
        client.PumpFrames(3);
        Assert.True(JToken.DeepEquals(time, client.Call("setTime").Require("omitted time")));
        Assert.Equal("INTERNAL_ERROR", client.Call("setTime", new JObject { ["timeInBars"] = null, ["timeInSecs"] = 9 }).ErrorCode);
        Assert.Equal("INTERNAL_ERROR", client.Call("setTime", new { timeInSecs = "invalid" }).ErrorCode);
        Assert.Equal(-2.5, client.Call("getContext").Require("unchanged time")["time"]!["timeInSecs"]!.Value<double>(), 8);
    }

    [Fact]
    public void Playback_UsesSpeedPrecedenceAndPlayingThreshold()
    {
        using var editor = Start();
        var client = editor.Client;
        foreach (var speed in new[] { -2.0, 0.001, 0.0 })
        {
            var result = client.Call("setPlayback", new { speed, playing = true }).Require("speed precedence");
            Assert.Equal(speed, result["playbackSpeed"]!.Value<double>());
            var time = client.Call("getContext").Require("playback context")["time"]!;
            Assert.Equal(speed, time["playbackSpeed"]!.Value<double>());
            Assert.Equal(Math.Abs(speed) > 0.001, time["isPlaying"]!.Value<bool>());
        }
        Assert.Equal(1, client.Call("setPlayback", new { playing = true }).Require("play")["playbackSpeed"]!.Value<double>());
        Assert.Equal(0, client.Call("setPlayback", new { playing = false }).Require("pause")["playbackSpeed"]!.Value<double>());
        Assert.Equal(0, client.Call("setPlayback").Require("omitted speed")["playbackSpeed"]!.Value<double>());
        Assert.Equal("INTERNAL_ERROR", client.Call("setPlayback", new JObject { ["speed"] = null, ["playing"] = true }).ErrorCode);
        Assert.Equal("INTERNAL_ERROR", client.Call("setPlayback", new JObject { ["playing"] = null }).ErrorCode);
        Assert.Equal(0, client.Call("setPlayback").Require("unchanged speed")["playbackSpeed"]!.Value<double>());
    }

    [Fact]
    public void PumpFrames_ClampsLowCountsAndDefersReplies()
    {
        using var editor = Start();
        var client = editor.Client;
        foreach (var arguments in new object[] { new { }, new { count = 0 }, new { count = -10 }, new { count = 3 } })
        {
            var before = client.Ping();
            var result = client.Call("pumpFrames", arguments);
            Assert.Empty(result.Require("frame pump"));
            Assert.True(result.Frame > before.Frame);
        }
        Assert.Equal("INTERNAL_ERROR", client.Call("pumpFrames", new JObject { ["count"] = null }).ErrorCode);
        using var wire = new OwnedWireConnection(editor.OwnedSession!);
        wire.Send(new JObject { ["id"] = "stall", ["method"] = "stallMainThread", ["seconds"] = 0.2 },
                  new JObject { ["id"] = "long", ["method"] = "pumpFrames", ["count"] = 5 },
                  new JObject { ["id"] = "short", ["method"] = "pumpFrames", ["count"] = 1 },
                  new JObject { ["id"] = "barrier", ["method"] = "ping" });
        var replies = new[] { wire.Read(), wire.Read(), wire.Read(), wire.Read() };
        Assert.Equal(new[] { "stall", "barrier", "short", "long" }, replies.Select(r => r["id"]!.Value<string>()));
        Assert.All(replies, r => Assert.True(r["ok"]!.Value<bool>()));
        Assert.True(replies[2]["frame"]!.Value<int>() >= replies[1]["frame"]!.Value<int>() + 1);
        Assert.True(replies[3]["frame"]!.Value<int>() >= replies[1]["frame"]!.Value<int>() + 5);
    }

    private EditorFixture Start()
    {
        var editor = OwnedEditorFixture.StartEditor(createProject: false);
        output.WriteLine("Evidence: " + editor.OwnedSession!.EvidenceDirectory);
        return editor;
    }
}
