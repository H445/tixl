using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Editor.IntegrationTests;

[Trait("Category", "Offline")]
public sealed class OwnedPathTests
{
    [Fact]
    public void SharedLaunchArguments_DoNotChangeLegacyProfileOrVisibility()
    {
        var executable = Path.Combine(Path.GetTempPath(), "TiXL.exe");
        var start = EditorFixture.CreateStartInfo(executable, 12345);
        Assert.Equal(["--debug-server", "12345", "--window", "1280x720", "--no-splash", "--no-reopen"], start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.False(start.CreateNoWindow);
        Assert.Equal(System.Diagnostics.ProcessWindowStyle.Normal, start.WindowStyle);
    }

    [Fact]
    public void ClaimVerifyAndCleanup_OnlyTouchOwnedPaths()
    {
        var parent = Path.Combine(Path.GetTempPath(), "bridge-path-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var paths = Paths(parent);
        var unrelated = Path.Combine(parent, "keep.txt");
        File.WriteAllText(unrelated, "unrelated");
        try
        {
            paths.Claim();
            paths.Verify(Report(paths));
            var wrong = Report(paths);
            wrong["projects"] = parent;
            Assert.Throws<InvalidDataException>(() => paths.Verify(wrong));
            var prefixCollision = Report(paths);
            prefixCollision["log"] = paths["settings"] + "-other/Log";
            Assert.Throws<InvalidDataException>(() => paths.Verify(prefixCollision));
            paths.Delete();
            Assert.All(paths.Roots, root => Assert.False(Directory.Exists(root)));
            Assert.Equal("unrelated", File.ReadAllText(unrelated));
        }
        finally
        {
            File.Delete(unrelated);
            Directory.Delete(parent);
        }
    }

    [Fact]
    public void ExistingRootsAndChangedOwnership_AreRejected()
    {
        var parent = Path.Combine(Path.GetTempPath(), "bridge-path-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var paths = Paths(parent);
        try
        {
            paths.Claim();
            Assert.Throws<IOException>(() => paths.Claim());
            var marker = Path.Combine(paths["settings"], ".debug-bridge-test-owner");
            File.WriteAllText(marker, "different owner");
            Assert.Throws<IOException>(() => paths.Delete());
            Assert.All(paths.Roots, root => Assert.True(Directory.Exists(root)));
            File.WriteAllText(marker, paths.RunId);
            paths.Delete();
        }
        finally
        {
            Directory.Delete(parent);
        }
    }

    [Fact]
    public void UnsafeProfileInputs_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new OwnedEditorPaths("4.4", "../escape", "a", "b", "c", "d"));
        Assert.Throws<InvalidOperationException>(() => new OwnedEditorPaths("4.4", Guid.NewGuid().ToString("N"), "relative", "b", "c", "d"));
    }

    private static OwnedEditorPaths Paths(string parent)
        => new("4.4", Guid.NewGuid().ToString("N"), parent, parent, parent, parent);

    private static JObject Report(OwnedEditorPaths paths) => new()
    {
        ["settings"] = paths["settings"], ["projects"] = paths["projects"],
        ["cache"] = paths["cache"], ["temp"] = paths["temp"], ["log"] = Path.Combine(paths["settings"], "Log"),
    };
}

[CollectionDefinition("OwnedEditor")]
public sealed class OwnedEditorCollection : ICollectionFixture<OwnedEditorFixture>;

public sealed class OwnedEditorFixture : IDisposable
{
    public OwnedEditorFixture()
    {
        var executable = Environment.GetEnvironmentVariable("TIXL_CONTRACT_EXE");
        var backend = Environment.GetEnvironmentVariable("TIXL_CONTRACT_BACKEND");
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(backend))
        {
            throw new InvalidOperationException("Owned contract tests require explicit TIXL_CONTRACT_EXE and TIXL_CONTRACT_BACKEND. Use --filter Category=Offline for offline tests.");
        }
        Editor = EditorFixture.CreateOwned(executable, backend);
    }

    public EditorFixture Editor { get; }
    public void Dispose() => Editor.Dispose();
}

[Collection("OwnedEditor")]
[Trait("Category", "OwnedEditor")]
public sealed class OwnedEditorSmokeTests(OwnedEditorFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void InspectDisposableProject_UsesExistingBridgeDiscovery()
    {
        var editor = fixture.Editor;
        var session = editor.OwnedSession!;
        output.WriteLine("Evidence: " + session.EvidenceDirectory);
        var context = editor.Client.Call("getContext").Require("getContext");
        Assert.True(context["hasOpenProject"]!.Value<bool>());
        Assert.Equal(session.HomeSymbolId.ToString(), context["compositionSymbolId"]!.Value<string>());
        var graph = editor.Client.Call("getGraphState", new { compositionId = session.HomeSymbolId, includeDefaults = true }).Require("getGraphState");
        Assert.Equal(session.HomeSymbolId.ToString(), graph["symbolId"]!.Value<string>());
        Assert.IsType<JArray>(graph["children"]);
        Assert.IsType<JArray>(graph["connections"]);
        Assert.True(Directory.Exists(session.ProjectDirectory));
        session.Paths.Verify(editor.Client.Call("getPaths").Require("getPaths"));
    }
}
