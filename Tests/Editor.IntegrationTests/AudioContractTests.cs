using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Collection("OwnedContracts")]
[Trait("Category", "OwnedEditor")]
public sealed class AudioContractTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("ContractGroup", "Audio")]
    public void AudioState_ReportsDevicesAndIdleStopWithoutCapture()
    {
        using var editor = Start();
        var state = editor.Client.Call("getAudioInputs").Require("audio inputs");
        foreach (var device in Assert.IsType<JArray>(state["devices"]))
        {
            Assert.Equal(JTokenType.String, device["name"]!.Type);
            Assert.Equal(JTokenType.Boolean, device["isLoopback"]!.Type);
        }
        Assert.Equal(JTokenType.Null, state["active"]!.Type);
        Assert.True(double.IsFinite(state["level"]!.Value<double>()));
        Assert.Equal(JTokenType.Null, editor.Client.Call("stopAudioRecording").Require("idle stop")["path"]!.Type);
        Assert.Equal(JTokenType.Null, editor.Client.Call("stopAudioRecording").Require("repeated idle stop")["path"]!.Type);
    }

    [Fact]
    [Trait("ContractGroup", "Audio")]
    public void AudioSelection_AcknowledgesUnknownDeviceWithoutOpeningIt()
    {
        using var editor = Start();
        var client = editor.Client;
        var device = "nonexistent-contract-" + Guid.NewGuid().ToString("N");
        Assert.DoesNotContain(client.Call("getAudioInputs").Require("device inventory")["devices"]!, d => d["name"]!.Value<string>() == device);
        var cursor = client.GetLatestLogSeq();
        Assert.Equal("ExternalDevice", client.Call("setAudioInput", new { device }).Require("unknown device accepted")["audioSource"]!.Value<string>());
        client.PumpFrames(3);
        Assert.Equal(JTokenType.Null, client.Call("getAudioInputs").Require("no capture")["active"]!.Type);
        Assert.Contains(client.GetLogTail(cursor, "warning"), e => e["message"]!.Value<string>()!.Contains(device));
        foreach (var arguments in new object[] { new { device = "" }, new { }, new JObject { ["device"] = null } })
            Assert.Equal("ProjectSoundTrack", client.Call("setAudioInput", arguments).Require("project audio")["audioSource"]!.Value<string>());
    }

    [Fact]
    [Trait("ContractGroup", "AudioDeviceAbsence")]
    public void Recording_WithoutDevicesReturnsNullAndNoFile()
    {
        using var editor = Start();
        var devices = Assert.IsType<JArray>(editor.Client.Call("getAudioInputs").Require("device inventory")["devices"]);
        Assert.True(devices.Count == 0, "Prerequisite: provision an audio device-free runner. Do not record a fallback device on a workstation.");
        var cursor = editor.Client.GetLatestLogSeq();
        Assert.Equal(JTokenType.Null, editor.Client.Call("startAudioRecording").Require("unavailable recording")["path"]!.Type);
        Assert.Equal(JTokenType.Null, editor.Client.Call("stopAudioRecording").Require("idle stop")["path"]!.Type);
        Assert.Contains(editor.Client.GetLogTail(cursor, "warning"), e => e["message"]!.Value<string>()!.Contains("no audio input device available"));
        Assert.False(Directory.Exists(Path.Combine(editor.OwnedSession!.Paths["temp"], "Recordings")));
    }

    private EditorFixture Start()
    {
        var editor = OwnedEditorFixture.StartEditor();
        output.WriteLine("Evidence: " + editor.OwnedSession!.EvidenceDirectory);
        return editor;
    }
}
