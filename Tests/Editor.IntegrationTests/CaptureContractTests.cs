using System.Diagnostics;
using Newtonsoft.Json.Linq;
using StbImageSharp;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Collection("OwnedContracts")]
[Trait("Category", "OwnedEditor")]
[Trait("ContractGroup", "Capture")]
public sealed class CaptureContractTests(ITestOutputHelper output)
{
    [Fact]
    public void Screenshot_ValidatesArgumentsAndRequiresOutput()
    {
        using var editor = Start(false);
        var client = editor.Client;
        Assert.Equal("MISSING_PARAM", client.Call("screenshot").ErrorCode);
        Assert.Equal("MISSING_PARAM", client.Call("screenshot", new { path = "  " }).ErrorCode);
        var path = Path.Combine(editor.OwnedSession!.Paths["temp"], "no-output.png");
        Assert.Equal("INVALID_PARAM", client.Call("screenshot", new { path, target = "Output" }).ErrorCode);
        Assert.Equal("NO_OUTPUT", client.Call("screenshot", new { path }).ErrorCode);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Screenshot_OutputFormatsAreReadableWhenReplyArrives()
    {
        using var editor = Start();
        PrepareTexture(editor);
        foreach (var suffix in new[] { ".png", ".JPG", ".jpeg" })
        {
            var path = Path.Combine(editor.OwnedSession!.Paths["temp"], "capture ü space" + suffix);
            Assert.False(File.Exists(path));
            var reply = editor.Client.Call("screenshot", new { path }).Require("capture output");
            Assert.Equal(path, reply["path"]!.Value<string>());
            using var file = File.OpenRead(path);
            var decoded = ImageResult.FromStream(file, ColorComponents.RedGreenBlueAlpha);
            Assert.Equal(64, decoded.Width);
            Assert.Equal(32, decoded.Height);
            Assert.True(decoded.Data[0] > 200 && decoded.Data[1] < 40 && decoded.Data[2] < 40);
            var signature = File.ReadAllBytes(path);
            if (suffix == ".JPG")
            {
                Assert.Equal(new byte[] { 255, 216 }, signature.Take(2));
            }
            else
            {
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, signature.Take(8));
            }
            Keep(editor, path);
        }
    }

    [Fact]
    public void Screenshot_FileWriteFailureCanStillReturnSuccess()
    {
        using var editor = Start();
        PrepareTexture(editor);
        var path = Path.Combine(editor.OwnedSession!.Paths["temp"], "absent-parent", "failed.png");
        var cursor = editor.Client.GetLatestLogSeq();
        Assert.Equal(path, editor.Client.Call("screenshot", new { path }).Require("current failed-save response")["path"]!.Value<string>());
        Assert.False(File.Exists(path));
        Assert.Contains(editor.Client.GetLogTail(cursor, "warning"), e => e["message"]!.Value<string>()!.Contains("Failed to export image"));
    }

    [Fact]
    public void Screenshot_OverlappingUiRequestsCanLoseTheFirstCallback()
    {
        using var editor = Start();
        var session = editor.OwnedSession!;
        using var wire = new OwnedWireConnection(session);
        var firstPath = Path.Combine(session.Paths["temp"], "first.png");
        var secondPath = Path.Combine(session.Paths["temp"], "second.png");
        wire.Send(new JObject { ["id"] = "stall", ["method"] = "stallMainThread", ["seconds"] = 0.2 },
                  new JObject { ["id"] = "first", ["method"] = "screenshot", ["target"] = "ui", ["path"] = firstPath },
                  new JObject { ["id"] = "second", ["method"] = "screenshot", ["target"] = "ui", ["path"] = secondPath },
                  new JObject { ["id"] = "barrier", ["method"] = "ping" });
        var replies = new[] { wire.Read(), wire.Read(), wire.Read() };
        Assert.Equal(new[] { "barrier", "second", "stall" }, replies.Select(r => r["id"]!.Value<string>()).Order());
        Assert.All(replies, r => Assert.True(r["ok"]!.Value<bool>()));
        Assert.False(File.Exists(firstPath));
        using (var file = File.OpenRead(secondPath))
        {
            Assert.True(ImageResult.FromStream(file).Width > 0);
        }
        Keep(editor, secondPath);
        wire.ReadTimeout = 300;
        Assert.Throws<IOException>(() => wire.Read());
        Assert.True(editor.Client.Ping().Ok);
    }

    [Fact]
    public void UiAndWindowCaptures_AreReadableAndWindowErrorsAreExplicit()
    {
        using var editor = Start();
        var session = editor.OwnedSession!;
        var uiPath = Path.Combine(session.Paths["temp"], "ui.png");
        editor.Client.Screenshot(uiPath, "ui");
        using (var file = File.OpenRead(uiPath))
        {
            var decoded = ImageResult.FromStream(file, ColorComponents.RedGreenBlueAlpha);
            Assert.True(decoded.Width > 0 && decoded.Height > 0);
            Assert.All(decoded.Data.Where((_, index) => index % 4 == 3), alpha => Assert.Equal(255, alpha));
            Assert.True(decoded.Data.Distinct().Count() > 10);
        }
        Keep(editor, uiPath);
        Assert.Equal("MISSING_PARAM", editor.Client.Call("screenshotWindow").ErrorCode);
        Assert.Equal("INVALID_PARAM", editor.Client.Call("screenshotWindow", new { path = uiPath, region = "unknown" }).ErrorCode);
        var windowPath = Path.Combine(session.Paths["temp"], "nested", "window.jpg");
        var capture = editor.Client.Call("screenshotWindow", new { path = windowPath, region = "graph" });
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("SCREENSHOT_FAILED", capture.ErrorCode);
            Assert.Contains("only available on Windows", capture.ErrorDetail);
            return;
        }
        Assert.Equal(windowPath, capture.Require("window graph capture")["path"]!.Value<string>());
        Assert.Equal(new byte[] { 255, 216 }, File.ReadAllBytes(windowPath).Take(2));
        using (var file = File.OpenRead(windowPath))
        {
            var decoded = ImageResult.FromStream(file);
            Assert.True(decoded.Width > 0 && decoded.Height > 0);
        }
        Keep(editor, windowPath);
        Assert.Equal("SCREENSHOT_FAILED", editor.Client.Call("screenshotWindow", new { path = session.Paths["temp"] }).ErrorCode);
    }

    [Fact]
    public void Render_ValidatesPathDurationAndReadiness()
    {
        using var editor = Start(false);
        var path = Path.Combine(editor.OwnedSession!.Paths["temp"], "no-output.mp4");
        Assert.Equal("BAD_PATH", editor.Client.Call("startRender").ErrorCode);
        Assert.Equal("BAD_PATH", editor.Client.Call("startRender", new { path = "relative.mp4", durationSecs = 1 }).ErrorCode);
        Assert.Equal("BAD_DURATION", editor.Client.Call("startRender", new { path }).ErrorCode);
        Assert.Equal("BAD_DURATION", editor.Client.Call("startRender", new { path, durationSecs = -1 }).ErrorCode);
        Assert.Equal("RENDER_NOT_READY", editor.Client.Call("startRender", new { path, durationSecs = 1, exportAudio = false }).ErrorCode);
        var status = editor.Client.Call("getRenderStatus").Require("idle render status");
        Assert.False(status["isExporting"]!.Value<bool>());
        Assert.Equal(0, status["frameIndex"]!.Value<int>());
        Assert.Equal(JTokenType.String, status["state"]!.Type);
        Assert.True(double.IsFinite(status["progress"]!.Value<double>()));
        Assert.Equal(JTokenType.String, editor.Client.Call("cancelRender").Require("idle cancel")["message"]!.Type);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Render_CompletionAndCancellationHaveSeparateEvidence()
    {
        using var editor = Start();
        var renderTarget = PrepareTexture(editor, 256, 256);
        var session = editor.OwnedSession!;
        var path = Path.Combine(session.Paths["temp"], "completed.mp4");
        var accepted = editor.Client.Call("startRender", new { path, durationSecs = 0.2, fps = 10, codec = "h264", exportAudio = false }).Require("start short render");
        Assert.Equal(2, accepted["frameCount"]!.Value<int>());
        var timer = Stopwatch.StartNew();
        JObject status;
        do
        {
            editor.Client.PumpFrames(2);
            status = editor.Client.Call("getRenderStatus").Require("render status");
        }
        while (status["isExporting"]!.Value<bool>() && timer.Elapsed < TimeSpan.FromSeconds(30));
        Assert.False(status["isExporting"]!.Value<bool>(), "Render did not finish within the test bound.");
        output.WriteLine("Completed status: " + status);
        Assert.True(File.Exists(path), "Accepted render must produce an artifact in this configured success case.");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 32);
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4));
        Keep(editor, path);
        VerifyExportedVideo(editor, path);
        editor.Client.Pin(renderTarget);
        editor.Client.PumpFrames(3);
        var cancelledPath = Path.Combine(session.Paths["temp"], "cancelled.mp4");
        editor.Client.Call("startRender", new { path = cancelledPath, durationSecs = 360, fps = 60, codec = "H264", exportAudio = false }).Require("start cancel case");
        Assert.True(editor.Client.Call("getRenderStatus").Require("active render")["isExporting"]!.Value<bool>());
        Assert.Equal("RENDER_NOT_READY", editor.Client.Call("startRender", new { path, durationSecs = 1, exportAudio = false }).ErrorCode);
        editor.Client.Call("cancelRender").Require("cancel");
        editor.Client.PumpFrames(2);
        Assert.False(editor.Client.Call("getRenderStatus").Require("cancelled status")["isExporting"]!.Value<bool>());
    }

    [Fact]
    public void Render_AcceptedRequestCanFailLaterWithoutAFile()
    {
        using var editor = Start();
        PrepareTexture(editor, 256, 256);
        var path = Path.Combine(editor.OwnedSession!.Paths["temp"], "directory.mp4");
        Directory.CreateDirectory(path);
        var cursor = editor.Client.GetLatestLogSeq();
        editor.Client.Call("startRender", new { path, durationSecs = 1, fps = 30, codec = "H264", exportAudio = false }).Require("accepted invalid file target");
        var timer = Stopwatch.StartNew();
        JObject status;
        do
        {
            editor.Client.PumpFrames(2);
            status = editor.Client.Call("getRenderStatus").Require("late failure status");
        }
        while (status["isExporting"]!.Value<bool>() && timer.Elapsed < TimeSpan.FromSeconds(30));
        Assert.False(status["isExporting"]!.Value<bool>());
        Assert.False(File.Exists(path));
        Assert.Contains("Can't render", status["message"]!.Value<string>());
        Assert.Contains(editor.Client.GetLogTail(cursor, "error"), e => e["message"]!.Value<string>()!.Contains("Can't render"));
    }

    private EditorFixture Start(bool project = true)
    {
        var editor = OwnedEditorFixture.StartEditor(project);
        output.WriteLine("Evidence: " + editor.OwnedSession!.EvidenceDirectory);
        return editor;
    }

    private static Guid PrepareTexture(EditorFixture editor, int width = 64, int height = 32)
    {
        var client = editor.Client;
        var target = client.AddOp("RenderTarget");
        client.SetInput(target, "Resolution", JObject.FromObject(new { X = width, Y = height }));
        client.SetInput(target, "Clear", new JValue(true));
        client.SetInput(target, "EnableUpdate", new JValue(true));
        client.SetVector4Input(target, "ClearColor", 1, 0, 0, 1);
        client.Pin(target);
        client.PumpFrames(5);
        var texture = client.Call("getOutput", new { childId = target, outputName = "ColorBuffer", update = true }).Require("render target");
        Assert.Equal(width, texture["value"]!["width"]!.Value<int>());
        return target;
    }

    private static void VerifyExportedVideo(EditorFixture editor, string path)
    {
        var client = editor.Client;
        var video = client.AddOp("VideoClip");
        client.SetInput(video, "Path", new JValue(path));
        client.Call("setPlayback", new { speed = 0 }).Require("pause video decode");
        client.Call("setTime", new { timeInSecs = 0 }).Require("first video frame");
        client.Pin(video);
        var timer = Stopwatch.StartNew();
        JToken frame;
        do
        {
            client.PumpFrames(2);
            frame = client.Call("getOutput", new { childId = video, outputName = "Texture", update = true }).Require("decode exported video")["value"]!;
        }
        while (frame.Type == JTokenType.Null && timer.Elapsed < TimeSpan.FromSeconds(20));
        Assert.IsType<JObject>(frame);
        Assert.Equal(256, frame["width"]!.Value<int>());
        Assert.Equal(256, frame["height"]!.Value<int>());
        client.PumpFrames(3);
        var imagePath = Path.Combine(editor.OwnedSession!.Paths["temp"], "decoded-video.png");
        client.Screenshot(imagePath);
        using var file = File.OpenRead(imagePath);
        var decoded = ImageResult.FromStream(file, ColorComponents.RedGreenBlueAlpha);
        Assert.Equal(256, decoded.Width);
        Assert.Equal(256, decoded.Height);
        Assert.True(decoded.Data[0] > 200 && decoded.Data[1] < 40 && decoded.Data[2] < 40);
        Keep(editor, imagePath);
    }

    private static void Keep(EditorFixture editor, string path)
        => File.Copy(path, Path.Combine(editor.OwnedSession!.EvidenceDirectory, Path.GetFileName(path)));
}
