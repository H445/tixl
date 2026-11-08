using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Collection("OwnedContracts")]
[Trait("Category", "OwnedEditor")]
[Trait("ContractGroup", "Projects")]
public sealed class ProjectContractTests(ITestOutputHelper output)
{
    [Fact]
    public void CreateAndOpen_UseOwnedRootsAndGuidPrecedence()
    {
        using var editor = Start();
        var session = editor.OwnedSession!;
        var client = editor.Client;
        Assert.True(Directory.Exists(session.ProjectDirectory));
        session.Paths.Verify(client.Call("getPaths").Require("getPaths"));
        var opened = client.Call("openProject", new { symbolId = session.HomeSymbolId, name = "missing", pinOutput = false }).Require("GUID precedence");
        Assert.Equal(session.HomeSymbolId.ToString(), opened["rootSymbolId"]!.Value<string>());
        Assert.False(opened["pinnedOutput"]!.Value<bool>());
        var shortName = Path.GetFileName(session.ProjectDirectory);
        var byName = client.Call("openProject", new { name = shortName.ToUpperInvariant(), pinOutput = false }).Require("case-insensitive short name");
        Assert.Equal(session.HomeSymbolId.ToString(), byName["rootSymbolId"]!.Value<string>());
        Assert.Equal("INVALID_PARAM", client.Call("openProject", new { symbolId = "invalid", name = shortName }).ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("openProject", new { symbolId = Guid.NewGuid(), name = shortName }).ErrorCode);
        Assert.Equal("NOT_FOUND", client.Call("openProject", new { name = "DefinitelyNoSuchProject" }).ErrorCode);
        Assert.Equal("MISSING_PARAM", client.Call("openProject").ErrorCode);
        foreach (var name in new[] { "", "   " })
        {
            Assert.Equal("MISSING_PARAM", client.Call("newProject", new { name }).ErrorCode);
        }
        Assert.Equal("MISSING_PARAM", client.Call("newProject").ErrorCode);
    }

    [Fact]
    public void Reload_BrokenOwnedSourceFailsAndRecoveryPreservesIds()
    {
        using var editor = Start();
        var session = editor.OwnedSession!;
        var client = editor.Client;
        var projectName = Path.GetFileName(session.ProjectDirectory);
        var child = client.AddOp("Add");
        client.SetInput(child, "Input1", new JValue(12.0));
        var source = Assert.Single(Directory.GetFiles(Path.Combine(session.ProjectDirectory, "Symbols"), "*.cs", SearchOption.AllDirectories));
        var original = File.ReadAllText(source);
        try
        {
            File.WriteAllText(source, original + "\nthis is not valid C#\n");
            var broken = client.Reload(projectName);
            Assert.False(broken.Ok);
            Assert.Equal("COMPILE_FAILED", broken.ErrorCode);
            Assert.Contains("error CS", broken.ErrorDetail);
        }
        finally
        {
            File.WriteAllText(source, original);
        }
        var restored = client.Reload(projectName).Require("reload restored source");
        Assert.True(restored["durationSeconds"]!.Value<double>() >= 0);
        client.PumpFrames(3);
        var graph = client.GetGraphState();
        Assert.Equal(session.HomeSymbolId.ToString(), graph["symbolId"]!.Value<string>());
        Assert.Contains(graph["children"]!, c => c["childId"]!.Value<string>() == child.ToString());
        Assert.Equal(12.0, client.Call("getOutput", new { childId = child, update = true }).Require("output after reload")["value"]!.Value<double>());
        client.Undo();
        Assert.Equal(0.0, client.Call("getOutput", new { childId = child, update = true }).Require("undo after reload")["value"]!.Value<double>());
        client.Call("redo").Require("redo after reload");
        Assert.Equal(12.0, client.Call("getOutput", new { childId = child, update = true }).Require("redo value")["value"]!.Value<double>());
    }

    [Fact]
    public void Reload_RequiresAnEditableLoadedProject()
    {
        using var editor = Start();
        Assert.Equal("MISSING_PARAM", editor.Client.Call("reload").ErrorCode);
        Assert.Equal("MISSING_PARAM", editor.Client.Reload("  ").ErrorCode);
        Assert.Equal("NOT_FOUND", editor.Client.Reload("DefinitelyNoSuchProject").ErrorCode);
        Assert.Equal("NOT_FOUND", editor.Client.Reload("Lib").ErrorCode);
    }

    private EditorFixture Start()
    {
        var editor = OwnedEditorFixture.StartEditor();
        output.WriteLine("Evidence: " + editor.OwnedSession!.EvidenceDirectory);
        return editor;
    }
}
