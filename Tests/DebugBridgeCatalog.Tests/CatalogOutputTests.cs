using System.Text;
using System.Text.Json;
using Xunit;

namespace DebugBridgeCatalog.Tests;

public sealed class CatalogOutputTests
{
    [Fact]
    public void Generation_IsDeterministicAndWorksWithoutEditorBinaries()
    {
        using var workspace = new Workspace();
        var catalog = CatalogRepository.ReadValidated(workspace.Root);
        var first = CatalogOutput.Render(catalog);
        CatalogOutput.Write(workspace.Root, first);
        var second = CatalogOutput.Render(CatalogRepository.ReadValidated(workspace.Root));
        Assert.Equal(first.Keys, second.Keys);
        foreach (var (path, content) in first)
        {
            Assert.Equal(content, second[path]);
            Assert.DoesNotContain('\r', content);
            Assert.Equal(Encoding.UTF8.GetBytes(content), File.ReadAllBytes(Path.Combine(workspace.Root, path)));
        }
        Assert.Empty(CatalogOutput.Check(workspace.Root, second));
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.dll", SearchOption.AllDirectories));
    }

    [Fact]
    public void Check_RejectsMissingStaleAndObsoleteOutputs()
    {
        using var workspace = new Workspace();
        var output = CatalogOutput.Render(CatalogRepository.ReadValidated(workspace.Root));
        Assert.Equal(2, CatalogOutput.Check(workspace.Root, output).Count);
        CatalogOutput.Write(workspace.Root, output);
        File.AppendAllText(Path.Combine(workspace.Root, CatalogOutput.ReferencePath), "manual edit");
        workspace.Write(".agentic/debug-bridge/obsolete.json", "{}");
        var errors = CatalogOutput.Check(workspace.Root, output);
        Assert.Contains(errors, e => e.StartsWith("Stale generated file:"));
        Assert.Contains(errors, e => e.StartsWith("Obsolete generated file:"));
        CatalogOutput.Write(workspace.Root, output);
        Assert.Single(CatalogOutput.Check(workspace.Root, output));
    }

    [Fact]
    public void Markdown_PreservesTypeNamesAndTableCells()
    {
        using var workspace = new Workspace();
        var catalog = CatalogRepository.ReadValidated(workspace.Root);
        catalog.Commands[0] = catalog.Commands[0] with
        {
            Arguments = [new("items", "List<int>", false, "A | B\nnext line", "Omitted; not null")],
            Result = "List<int> and `opaque` & other",
        };
        var markdown = CatalogOutput.Render(catalog)[CatalogOutput.ReferencePath];
        Assert.Contains("List&lt;int&gt;", markdown);
        Assert.Contains("A &#124; B<br>next line", markdown);
        Assert.Contains("&#96;opaque&#96; &amp; other", markdown);
        Assert.Contains("execution status is recorded separately", markdown);
    }

    [Fact]
    public void NewCommand_RequiresMetadataAndVerificationBeforeGeneration()
    {
        using var workspace = new Workspace();
        workspace.Write("Editor/App/DebugProtocol/DebugServer.cs", Workspace.Source.Replace("default:", "case \"newCommand\": break; default:"));
        Assert.Contains("missing command 'newCommand'", Assert.Throws<InvalidDataException>(() => CatalogRepository.ReadValidated(workspace.Root)).Message);
        var catalog = Workspace.Catalog();
        catalog = catalog with { Commands = [..catalog.Commands, Workspace.Command("newCommand", "Contracts.Probe.NewCommand")] };
        workspace.WriteCatalog(catalog);
        Assert.Contains("verification 'Contracts.Probe.NewCommand'", Assert.Throws<InvalidDataException>(() => CatalogRepository.ReadValidated(workspace.Root)).Message);
        workspace.Write("Tests/NewCommand.cs", "namespace Contracts; class Probe { [Xunit.Theory] public void NewCommand() {} }");
        var output = CatalogOutput.Render(CatalogRepository.ReadValidated(workspace.Root));
        Assert.Contains("## newCommand", output[CatalogOutput.ReferencePath]);
        using var json = JsonDocument.Parse(output[CatalogOutput.JsonPath]);
        Assert.Equal(2, json.RootElement.GetProperty("commands").GetArrayLength());
    }

    [Fact]
    public void Verification_RejectsRenamedUnannotatedAndDuplicateTests()
    {
        using var workspace = new Workspace();
        foreach (var source in new[] { "namespace Contracts; class Probe { [Xunit.Fact] public void Renamed() {} }",
                                      "namespace Contracts; class Probe { public void Ping() {} }" })
        {
            workspace.Write("Tests/Probe.cs", source);
            Assert.Contains("verification 'Contracts.Probe.Ping'", Assert.Throws<InvalidDataException>(() => CatalogRepository.ReadValidated(workspace.Root)).Message);
        }
        workspace.Write("Tests/Probe.cs", Workspace.Test);
        workspace.Write("Tests/Duplicate.cs", Workspace.Test);
        Assert.Contains("exactly one", Assert.Throws<InvalidDataException>(() => CatalogRepository.ReadValidated(workspace.Root)).Message);
    }

    [Fact]
    public void Verification_IgnoresBuildOutputs()
    {
        using var workspace = new Workspace();
        workspace.Write("Tests/bin/Probe.cs", Workspace.Test);
        workspace.Write("Tests/obj/Probe.cs", Workspace.Test);
        Assert.Single(CatalogRepository.ReadValidated(workspace.Root).Commands);
    }

    [Fact]
    public void GroupLoading_RejectsDuplicatesAndInconsistentTargetDefinitions()
    {
        using var workspace = new Workspace();
        var directory = Path.Combine(workspace.Root, "Tools/DebugBridgeCatalog/Catalog");
        workspace.Write("Tools/DebugBridgeCatalog/Catalog/second.json", JsonSerializer.Serialize(Workspace.Catalog(), CatalogDocument.JsonOptions));
        Assert.Contains("Duplicate catalog command", Assert.Throws<InvalidDataException>(() => CatalogRepository.Load(directory)).Message);
        var differentTarget = Workspace.Catalog() with { Targets = [new("windows", ["OTHER_SYMBOL"])] };
        workspace.Write("Tools/DebugBridgeCatalog/Catalog/second.json", JsonSerializer.Serialize(differentTarget, CatalogDocument.JsonOptions));
        Assert.Contains("target definitions", Assert.Throws<InvalidDataException>(() => CatalogRepository.Load(directory)).Message);
    }

    private sealed class Workspace : IDisposable
    {
        internal Workspace()
        {
            Root = Directory.CreateTempSubdirectory("tixl-catalog-test-").FullName;
            Write("Editor/App/DebugProtocol/DebugServer.cs", Source);
            Write("Tests/Probe.cs", Test);
            WriteCatalog(Catalog());
        }

        internal string Root { get; }
        internal const string Source = "class Server { void ExecuteMethod(string method) { switch(method) { case \"ping\": break; default: break; } } }";
        internal const string Test = "namespace Contracts; class Probe { [Xunit.Fact] public void Ping() {} }";
        internal static CommandContract Command(string name, string verification)
            => new(name, "Test command", ["windows"], [], "Object", [], "No mutation", "Immediate", [verification]);
        internal static CatalogDocument Catalog()
            => new("1.0", 1, [new("windows", [])], [Command("ping", "Contracts.Probe.Ping")]);
        internal void WriteCatalog(CatalogDocument catalog)
            => Write("Tools/DebugBridgeCatalog/Catalog/commands.json", JsonSerializer.Serialize(catalog, CatalogDocument.JsonOptions));
        internal void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        public void Dispose()
        {
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != parent || !Path.GetFileName(Root).StartsWith("tixl-catalog-test-", StringComparison.Ordinal))
                throw new IOException("Refusing cleanup outside the allocated test directory.");
            Directory.Delete(Root, recursive: true);
        }
    }
}
