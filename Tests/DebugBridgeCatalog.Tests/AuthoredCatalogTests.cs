using System.Text.Json;
using Xunit;

namespace DebugBridgeCatalog.Tests;

public sealed class AuthoredCatalogTests
{
    [Fact]
    public void AuthoredGroups_HaveValidMetadataAndOnlyRealCommands()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Catalog"), "*.json");
        Assert.NotEmpty(files);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Source", "DebugServer.cs"));
        foreach (var file in files)
        {
            var catalog = JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(file), CatalogDocument.JsonOptions)!;
            Assert.Empty(CatalogValidator.ValidateMetadata(catalog));
            foreach (var command in catalog.Commands)
            {
                Assert.True(names.Add(command.Name), "Duplicate command across authored groups: " + command.Name);
                Assert.True(command.Extensions?.ContainsKey("exampleRequest"));
                Assert.Equal(command.Name, command.Extensions!["exampleRequest"].GetProperty("method").GetString());
                foreach (var target in catalog.Targets.Where(t => command.Targets.Contains(t.Name)))
                {
                    Assert.Contains(command.Name, CommandInventory.Read(source, target.Symbols));
                }
            }
        }
    }
}
