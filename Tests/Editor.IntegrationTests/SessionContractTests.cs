using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Trait("Category", "OwnedEditor")]
[Trait("ContractGroup", "Session")]
[Collection("OwnedContracts")]
public sealed class SessionContractTests(ITestOutputHelper output)
{
    private EditorFixture Start(bool project = false)
    {
        var editor = OwnedEditorFixture.StartEditor(project);
        output.WriteLine("Evidence: " + editor.OwnedSession!.EvidenceDirectory);
        return editor;
    }

    [Fact]
    public void ReadOnlyDiagnostics_WorkWithoutProject()
    {
        using var editor = Start();
        var client = editor.Client;
        var ping = client.Ping();
        Assert.True(ping.Ok);
        Assert.Empty(ping.Require("ping"));
        Assert.True(ping.Frame >= 0);
        Assert.True(ping.PlaybackFrame >= 0);
        Assert.True(ping.StructureVersion >= 0);
        var version = client.Call("getVersion").Require("getVersion");
        Assert.Equal(1, version["protocolVersion"]!.Value<int>());
        Assert.True(Version.TryParse(version["editorVersion"]!.Value<string>(), out _));
        editor.OwnedSession!.Paths.Verify(client.Call("getPaths").Require("getPaths"));
        var structure = client.Call("getStructureVersion").Require("getStructureVersion");
        Assert.Equal(JTokenType.Integer, structure["symbolStructureVersion"]!.Type);
        var context = client.Call("getContext").Require("getContext");
        Assert.False(context["hasOpenProject"]!.Value<bool>());
        Assert.False(context.ContainsKey("compositionSymbolId"));
        Assert.False(context.ContainsKey("selectedChildren"));
        Assert.IsType<JObject>(context["time"]);
        foreach (var field in new[] { "timeInBars", "timeInSecs", "playbackSpeed", "bpm" })
        {
            Assert.True(double.IsFinite(context["time"]![field]!.Value<double>()));
        }
        Assert.Equal(JTokenType.Boolean, context["time"]!["isPlaying"]!.Type);
    }

    [Fact]
    public void Context_ReportsOwnedProject()
    {
        using var editor = Start(project: true);
        var context = editor.Client.Call("getContext").Require("getContext");
        Assert.True(context["hasOpenProject"]!.Value<bool>());
        Assert.Equal(editor.OwnedSession!.HomeSymbolId.ToString(), context["compositionSymbolId"]!.Value<string>());
        Assert.NotEmpty(context["compositionName"]!.Value<string>()!);
        Assert.IsType<JArray>(context["compositionPath"]);
        Assert.IsType<JArray>(context["selectedChildren"]);
    }

    [Fact]
    public void AgentState_AcceptsExactValuesAndRejectsInvalid()
    {
        using var editor = Start();
        foreach (var state in new[] { "busy", "ready", "" })
        {
            var result = editor.Client.Call("setAgentState", new { state, note = "contract test" }).Require("setAgentState");
            Assert.Equal(state, result["state"]!.Value<string>());
            Assert.True(editor.Client.Ping().Ok);
        }
        Assert.Equal("", editor.Client.Call("setAgentState").Require("setAgentState")["state"]!.Value<string>());
        foreach (var state in new[] { "READY", "invalid" })
        {
            var failure = editor.Client.Call("setAgentState", new { state });
            Assert.False(failure.Ok);
            Assert.Equal("INVALID_PARAM", failure.ErrorCode);
        }
    }

    [Fact]
    public void Logs_FilterBoundAndAdvanceCursors()
    {
        using var editor = Start();
        var initial = editor.Client.Call("getLogTail", new { maxCount = 5 }).Require("getLogTail");
        var entries = Assert.IsType<JArray>(initial["entries"]);
        Assert.InRange(entries.Count, 0, 5);
        var seqs = entries.Select(e => e["seq"]!.Value<long>()).ToArray();
        Assert.Equal(seqs.Order().ToArray(), seqs);
        Assert.All(entries, e =>
        {
            Assert.Equal(JTokenType.String, e["time"]!.Type);
            Assert.Equal(JTokenType.String, e["message"]!.Type);
            Assert.True(e["sourceId"]!.Type is JTokenType.String or JTokenType.Null);
        });
        var cursor = initial["latestSeq"]!.Value<long>();
        editor.Client.Call("stallMainThread", new { seconds = 0 }).Require("stallMainThread");
        var later = editor.Client.Call("getLogTail", new { sinceSeq = cursor }).Require("getLogTail");
        Assert.All((JArray)later["entries"]!, e => Assert.True(e["seq"]!.Value<long>() > cursor));
        Assert.Contains((JArray)later["entries"]!, e => e["message"]!.Value<string>()!.Contains("Stalling main thread"));
        foreach (var count in new[] { 0, -1 })
        {
            Assert.Empty((JArray)editor.Client.Call("getLogTail", new { maxCount = count }).Require("getLogTail")["entries"]!);
        }
        var errors = editor.Client.Call("getLogTail", new { minLevel = "ERROR", maxCount = 100 }).Require("getLogTail");
        Assert.All((JArray)errors["entries"]!, e => Assert.Equal("Error", e["level"]!.Value<string>()));
    }

    [Fact]
    public void MetricsAndSamples_ReportWarmupAndRelease()
    {
        using var editor = Start();
        var client = editor.Client;
        var metrics = client.GetMetrics();
        Assert.True(metrics["frameDeltaSeconds"]!.Value<double>() > 0);
        foreach (var field in new[] { "fps", "framePeriodMs", "refreshRateHz", "visualDeltaMs", "vblanksPerFrame",
                                      "frameSpeedFactor", "gpuFrameMs", "queuedFrames", "gcTotalMemoryMb", "stateStackDepth", "liveGpuResources" })
        {
            Assert.True(double.IsFinite(metrics[field]!.Value<double>()));
        }
        Assert.Equal(JTokenType.String, metrics["presentationMode"]!.Type);
        Assert.IsType<JObject>(metrics["renderStats"]);
        Assert.True(metrics["gpuMemory"]!.Type is JTokenType.Null or JTokenType.Object);
        if (metrics["gpuMemory"] is JObject memory)
        {
            Assert.True(memory["currentUsageMb"]!.Value<double>() >= 0);
            Assert.True(memory["budgetMb"]!.Value<double>() >= 0);
        }
        var before = client.Call("getFrameSamples").Require("getFrameSamples");
        AssertSamples(before);
        var released = client.Call("releaseGpuResources");
        Assert.Empty(released.Require("releaseGpuResources"));
        client.PumpFrames(3);
        var after = client.Call("getFrameSamples").Require("getFrameSamples");
        AssertSamples(after);
        Assert.True(after["totalFrames"]!.Value<long>() > before["totalFrames"]!.Value<long>());
        Assert.True(client.Ping().Frame > released.Frame);
        Assert.True(client.GetMetrics()["liveGpuResources"]!.Value<int>() >= 0);
    }

    private static void AssertSamples(JObject result)
    {
        foreach (var field in new[] { "frameMs", "cpuMs", "presentMs" })
        {
            var samples = Assert.IsType<JArray>(result[field]);
            Assert.All(samples, value =>
            {
                var duration = value.Value<double>();
                Assert.True(double.IsFinite(duration));
                Assert.True(duration >= 0);
                Assert.Equal(Math.Round(duration, 2), duration);
            });
        }
        Assert.True(result["totalFrames"]!.Value<long>() >= 0);
    }

    [Fact]
    public void ReleaseGpuResources_ReleasesAndRebuildsAnUnpinnedRenderTarget()
    {
        using var editor = Start(project: true);
        var client = editor.Client;
        var target = client.AddOp("RenderTarget");
        client.SetInput(target, "Resolution", JObject.FromObject(new { X = 64, Y = 32 }));
        client.SetInput(target, "Clear", new JValue(true));
        client.SetInput(target, "EnableUpdate", new JValue(true));
        client.SetInput(target, "WithDepthBuffer", new JValue(false));
        var allocated = client.Call("getOutput", new { childId = target, outputName = "ColorBuffer", update = true }).Require("allocate render target");
        Assert.Equal(64, allocated["value"]!["width"]!.Value<int>());
        Assert.Equal(32, allocated["value"]!["height"]!.Value<int>());
        Assert.False(allocated["value"]!["disposed"]!.Value<bool>());
        var cursor = client.GetLatestLogSeq();
        client.Call("releaseGpuResources").Require("release render target");
        client.PumpFrames(2);
        var released = client.Call("getOutput", new { childId = target, outputName = "ColorBuffer" }).Require("cached released texture");
        Assert.Equal(JTokenType.Null, released["value"]!.Type);
        Assert.Contains(client.GetLogTail(cursor), e => e["message"]!.Value<string>()!.Contains("Released GPU resources of")
                                                     && e["message"]!.Value<string>()!.Contains("on request"));
        var rebuilt = client.Call("getOutput", new { childId = target, outputName = "ColorBuffer", update = true }).Require("rebuild texture");
        Assert.True(JToken.DeepEquals(allocated["value"], rebuilt["value"]));
    }

    [Fact]
    public void ShaderErrors_ClearAndReadVariants()
    {
        using var editor = Start();
        Assert.True(editor.Client.Call("getShaderErrors", new { clear = true, includeSource = true }).Require("clear")["cleared"]!.Value<bool>());
        foreach (var includeSource in new[] { false, true })
        {
            var result = editor.Client.Call("getShaderErrors", new { includeSource }).Require("getShaderErrors");
            Assert.Equal(0, result["count"]!.Value<int>());
            Assert.Empty(Assert.IsType<JArray>(result["failures"]));
            Assert.False(result.ContainsKey("cleared"));
        }
    }

    [Fact]
    public void ShaderErrors_ReportSourceNewestFirstAndMissingFile()
    {
        using var editor = Start(project: true);
        var client = editor.Client;
        client.Call("getShaderErrors", new { clear = true }).Require("clear shader errors");
        var markers = new[] { "ContractInvalidFirst", "ContractInvalidSecond" };
        foreach (var marker in markers)
        {
            var shader = client.AddOp("PixelShaderFromSource");
            client.SetInput(shader, "DebugName", new JValue(marker));
            client.SetInput(shader, "EntryPoint", new JValue("psMain"));
            client.SetInput(shader, "ShaderSource", new JValue($"float4 psMain() : SV_Target {{ return {marker}; }}"));
            var evaluated = client.Call("getOutput", new { childId = shader, update = true }).Require("evaluate invalid shader");
            Assert.Equal(JTokenType.Null, evaluated["value"]!.Type);
        }
        var withoutSource = client.Call("getShaderErrors").Require("shader summaries");
        var summaries = Assert.IsType<JArray>(withoutSource["failures"]);
        Assert.Equal(2, withoutSource["count"]!.Value<int>());
        Assert.Equal(markers.Reverse(), summaries.Select(f => f["name"]!.Value<string>()));
        Assert.All(summaries, f => Assert.Null(f["source"]));
        var withSource = client.Call("getShaderErrors", new { includeSource = true }).Require("shader source");
        var latest = (JObject)withSource["failures"]![0]!;
        Assert.Contains(markers[1], latest["source"]!.Value<string>());
        Assert.Equal("psMain", latest["entryPoint"]!.Value<string>());
        Assert.NotEmpty(latest["reason"]!.Value<string>()!);
        var sourcePath = latest["sourcePath"]!.Value<string>()!;
        var relative = Path.GetRelativePath(editor.OwnedSession!.Paths["settings"], sourcePath);
        Assert.False(Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar), "Only delete source captured under the owned settings root.");
        File.Delete(sourcePath);
        var missing = client.Call("getShaderErrors", new { includeSource = true }).Require("missing source")["failures"]![0]!;
        Assert.NotEmpty(missing["sourceError"]!.Value<string>()!);
        Assert.Null(missing["source"]);
        client.Call("getShaderErrors", new { clear = true }).Require("clear populated errors");
        Assert.Empty((JArray)client.Call("getShaderErrors").Require("cleared errors")["failures"]!);
    }

    [Fact]
    public void Stall_ClampsNegativeAndWaitsForPositiveDuration()
    {
        using var editor = Start();
        Assert.Empty(editor.Client.Call("stallMainThread", new { seconds = -100 }).Require("stallMainThread"));
        var timer = Stopwatch.StartNew();
        Assert.Empty(editor.Client.Call("stallMainThread", new { seconds = 0.12, estimateKey = "bridge-contract", message = "bounded stall" }).Require("stallMainThread"));
        Assert.True(timer.Elapsed >= TimeSpan.FromMilliseconds(100));
        Assert.True(editor.Client.Ping().Ok);
    }

    [Fact]
    public void Settings_ClampReportPersistAndShutdown()
    {
        using var editor = Start();
        var client = editor.Client;
        var initial = client.Call("setVSync").Require("setVSync")["useVSync"]!.Value<bool>();
        Assert.Equal(!initial, client.Call("setVSync", new { value = !initial }).Require("setVSync")["useVSync"]!.Value<bool>());
        Assert.Equal("INTERNAL_ERROR", client.Call("setVSync", new { value = (bool?)null }).ErrorCode);
        Assert.Equal(!initial, client.Call("setVSync").Require("setVSync")["useVSync"]!.Value<bool>());
        Assert.Equal(0, client.Call("setTargetFrameRate", new { value = -20 }).Require("setTargetFrameRate")["targetFrameRate"]!.Value<int>());
        Assert.Equal(0, client.Call("setTargetFrameRate").Require("setTargetFrameRate")["targetFrameRate"]!.Value<int>());
        Assert.Equal("INTERNAL_ERROR", client.Call("setTargetFrameRate", new { value = (int?)null }).ErrorCode);
        Assert.Equal(2, client.Call("setFrameLatency", new { value = 99 }).Require("setFrameLatency")["frameLatency"]!.Value<int>());
        Assert.Equal(1, client.Call("setFrameLatency", new { value = -1 }).Require("setFrameLatency")["frameLatency"]!.Value<int>());
        Assert.Equal("INTERNAL_ERROR", client.Call("setFrameLatency", new { value = (int?)null }).ErrorCode);
        Assert.Equal(1, client.Call("setFrameLatency").Require("setFrameLatency")["frameLatency"]!.Value<int>());
        client.Shutdown();
        Assert.True(editor.OwnedSession!.WaitForExit(TimeSpan.FromSeconds(20)), "Shutdown must terminate the owned process.");
        var persisted = JObject.Parse(File.ReadAllText(Path.Combine(editor.OwnedSession.Paths["settings"], "userSettings.json")));
        Assert.Equal(!initial, persisted["UseVSync"]!.Value<bool>());
        Assert.Equal(0, persisted["TargetFrameRate"]!.Value<int>());
        Assert.Equal(1, persisted["FrameLatency"]!.Value<int>());
    }
}
