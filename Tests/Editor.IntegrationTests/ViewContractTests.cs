using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Collection("OwnedContracts")]
[Trait("Category", "OwnedEditor")]
[Trait("ContractGroup", "Views")]
public sealed class ViewContractTests(ITestOutputHelper output)
{
    [Fact]
    public void GraphCommands_RejectMissingComposition()
    {
        using var editor = Start(false);
        foreach (var method in new[] { "getGraphView", "setGraphView", "focusGraphView", "layoutSelection", "pin" })
        {
            Assert.Equal("NO_COMPOSITION", editor.Client.Call(method).ErrorCode);
        }
    }

    [Fact]
    public void WindowAndPin_UseExistingViewsAndReportSelection()
    {
        using var editor = Start();
        var client = editor.Client;
        Assert.Equal("MISSING_PARAM", client.Call("openWindow").ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("openWindow", new { name = "DefinitelyNoWindow" }).ErrorCode);
        Assert.Equal("SettingsWindow", client.Call("openWindow", new { name = "settingswindow", section = "Diagnostics" }).Require("show settings")["window"]!.Value<string>());
        Assert.Equal("INVALID_PARAM", client.Call("openWindow", new { name = "SettingsWindow", section = "DefinitelyNoSection" }).ErrorCode);
        client.Call("openWindow", new { name = "OutputWindow", section = "ignored for other windows" }).Require("show output");
        var child = client.AddOp("Add");
        client.Pin(child);
        client.PumpFrames(2);
        var context = client.Call("getContext").Require("pinned context");
        Assert.True(context["outputView"]!["isPinned"]!.Value<bool>());
        Assert.Equal(child.ToString(), context["outputView"]!["childId"]!.Value<string>());
        Assert.Equal("NOT_FOUND", client.Call("pin", new { childId = Guid.NewGuid() }).ErrorCode);
        Assert.Empty(client.Call("resetView").Require("reset view"));
        Assert.Equal("INVALID_PARAM", client.Call("outputSetup", new { mode = "DefinitelyNoMode" }).ErrorCode);
    }

    [Fact]
    public void GraphScope_AbsoluteRelativeAndDeferredArea()
    {
        using var editor = Start();
        var client = editor.Client;
        var scope = client.Call("setGraphView", new { centerX = 200.0, centerY = -150.0, scale = 1.25 }).Require("absolute scope");
        AssertScope(scope, 200, -150, 1.25);
        var relative = client.Call("setGraphView", new { zoomBy = 2.0, scrollByX = 10.0, scrollByY = 20.0 }).Require("relative scope");
        AssertScope(relative, 210, -130, 2.5);
        var smooth = client.Call("setGraphView", new { centerX = -75.0, centerY = 25.0, scale = 1.0, smooth = true }).Require("smooth target");
        AssertScope(smooth, -75, 25, 1);
        foreach (var scale in new[] { 0.0, 0.001, -1.0, 101.0 })
        {
            Assert.Equal("INVALID_PARAM", client.Call("setGraphView", new { scale }).ErrorCode);
        }
        Assert.Equal("INVALID_PARAM", client.Call("setGraphView", new { area = new { minX = 1, minY = 0, maxX = 1, maxY = 10 } }).ErrorCode);
        var area = client.Call("setGraphView", new { area = new { minX = 0, minY = 0, maxX = 400, maxY = 200 }, scale = -1 }).Require("area precedence");
        Assert.True(area["appliesNextFrame"]!.Value<bool>());
        client.PumpFrames(3);
        var applied = client.Call("getGraphView").Require("applied area");
        Assert.True(applied["windowWidth"]!.Value<double>() > 0);
        Assert.True(applied["windowHeight"]!.Value<double>() > 0);
        var visible = applied["visibleArea"]!;
        Assert.True(visible["minX"]!.Value<double>() <= 1);
        Assert.True(visible["minY"]!.Value<double>() <= 1);
        Assert.True(visible["maxX"]!.Value<double>() >= 399);
        Assert.True(visible["maxY"]!.Value<double>() >= 199);
    }

    [Fact]
    public void FocusGraph_ValidatesIdsAndAllClearsSelection()
    {
        using var editor = Start();
        var client = editor.Client;
        Assert.Equal("NOTHING_TO_FOCUS", client.Call("focusGraphView", new { childIds = Array.Empty<string>() }).ErrorCode);
        Assert.Equal("INVALID_PARAM", client.Call("focusGraphView", new { childIds = new[] { "invalid" } }).ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("focusGraphView", new { childIds = new[] { Guid.NewGuid() } }).ErrorCode);
        var child = client.AddOp("Add", posX: 500, posY: 250);
        client.Select(child);
        var explicitChild = client.Call("focusGraphView", new { childIds = new[] { child }, padding = 0 }).Require("focus explicit child");
        Assert.True(explicitChild["appliesNextFrame"]!.Value<bool>());
        Assert.Equal(500.0, explicitChild["area"]!["minX"]!.Value<double>());
        Assert.Equal(250.0, explicitChild["area"]!["minY"]!.Value<double>());
        var all = client.Call("focusGraphView", new { all = true }).Require("focus all");
        Assert.True(all["area"]!["minX"]!.Value<double>() <= 400);
        client.PumpFrames(3);
        Assert.Empty((JArray)client.Call("getContext").Require("cleared selection")["selectedChildren"]!);
    }

    [Fact]
    public void LayoutSelection_CharacterizesStaleViewAndUndoRedoRefresh()
    {
        using var editor = Start();
        var client = editor.Client;
        client.Call("select").Require("clear selection");
        Assert.False(client.Call("layoutSelection").Require("empty layout")["changed"]!.Value<bool>());
        var source = client.AddOp("Add", posX: 900, posY: 900);
        var target = client.AddOp("Add", posX: 0, posY: 0);
        client.ConnectOps(source, "Result", target, "Input1");
        client.Call("focusGraphView", new { childIds = new[] { source, target } }).Require("frame layout nodes");
        client.PumpFrames(3);
        client.Select(target);
        client.PumpFrames(3);
        var before = client.GetGraphState();
        var stale = client.Call("layoutSelection").Require("layout before view refresh");
        Assert.False(stale["changed"]!.Value<bool>());
        Assert.Equal(new[] { target.ToString() }, stale["selectedIds"]!.Values<string>());
        client.Undo();
        client.PumpFrames(2);
        client.Call("redo").Require("restore connection and refresh graph view");
        client.PumpFrames(3);
        client.Select(target);
        var layout = client.Call("layoutSelection").Require("layout inputs");
        if (!layout["changed"]!.Value<bool>())
        {
            output.WriteLine("Before: " + before);
            output.WriteLine("Context: " + client.Call("getContext").Require("layout context"));
            output.WriteLine("Layout: " + layout);
            var screenshot = Path.Combine(editor.OwnedSession!.Paths["temp"], "layout.png");
            client.Screenshot(screenshot, "ui");
            File.Copy(screenshot, Path.Combine(editor.OwnedSession.EvidenceDirectory, "layout.png"));
        }
        Assert.True(layout["changed"]!.Value<bool>());
        Assert.Contains(source.ToString(), layout["selectedIds"]!.Values<string>());
        Assert.Contains(target.ToString(), layout["selectedIds"]!.Values<string>());
        var after = client.GetGraphState();
        Assert.True(JToken.DeepEquals(before["connections"], after["connections"]));
        var moved = Assert.Single(after["children"]!, c => c["childId"]!.Value<string>() == source.ToString());
        Assert.True(moved["posX"]!.Value<double>() != 900 || moved["posY"]!.Value<double>() != 900);
        client.Undo();
        var restored = Assert.Single(client.GetGraphState()["children"]!, c => c["childId"]!.Value<string>() == source.ToString());
        Assert.Equal(900.0, restored["posX"]!.Value<double>());
        Assert.Equal(900.0, restored["posY"]!.Value<double>());
    }

    private EditorFixture Start(bool project = true)
    {
        var editor = OwnedEditorFixture.StartEditor(project);
        output.WriteLine("Evidence: " + editor.OwnedSession!.EvidenceDirectory);
        return editor;
    }

    private static void AssertScope(JObject scope, double centerX, double centerY, double scale)
    {
        Assert.Equal(scale, scope["scale"]!.Value<double>(), precision: 4);
        var area = scope["visibleArea"]!;
        Assert.Equal(centerX, (area["minX"]!.Value<double>() + area["maxX"]!.Value<double>()) / 2, precision: 3);
        Assert.Equal(centerY, (area["minY"]!.Value<double>() + area["maxY"]!.Value<double>()) / 2, precision: 3);
    }
}
