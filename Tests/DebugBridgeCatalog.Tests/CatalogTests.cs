using System.Text.Json;
using DebugBridgeCatalog;
using Xunit;

namespace DebugBridgeCatalog.Tests;

public sealed class CatalogTests
{
    private const string Source = """
        class Server {
            void ExecuteMethod(string method) {
                switch (method) {
                    case "ping": break;
                    case "inspect": case "inspectAlias": break;
        #if WINDOWS
                    case "capture": break;
        #endif
                    case null: break;
                    default: break;
                }
            }
        }
        """;

    private static CommandContract Command(string name, string[]? targets = null)
        => new(name, "Reviewed description", targets ?? ["portable", "windows"], [], "Object response",
               [], "No mutation", "Immediate", ["test." + name]);

    private static CatalogDocument Catalog()
        => new("1.0", 1, [new("portable", []), new("windows", ["WINDOWS"])],
               [Command("ping"), Command("inspect"), Command("inspectAlias") with { AliasOf = "inspect" },
                Command("capture", ["windows"])]);

    [Fact]
    public void Inventory_UsesTargetSymbolsAndPreservesAliases()
    {
        Assert.Equal(["inspect", "inspectAlias", "ping"], CommandInventory.Read(Source, []));
        Assert.Equal(["capture", "inspect", "inspectAlias", "ping"], CommandInventory.Read(Source, ["WINDOWS"]));
        Assert.Empty(CatalogValidator.Validate(Catalog(), Source));
    }

    [Theory]
    [InlineData("case \"newCommand\": break;", "newCommand")]
    [InlineData("case \"renamed\": break;", "renamed")]
    public void AddedOrRenamedCommand_RequiresMetadata(string replacement, string expected)
    {
        var source = Source.Replace("case \"ping\": break;", replacement);
        var errors = CatalogValidator.Validate(Catalog(), source);
        Assert.Contains(errors, e => e.Contains("missing command '" + expected + "'"));
        Assert.Contains(errors, e => e.Contains("stale command 'ping'"));
    }

    [Fact]
    public void Addition_CanBeDescribedWithoutChangingValidator()
    {
        var source = Source.Replace("case null:", "case \"newCommand\": break; case null:");
        var catalog = Catalog();
        Assert.Contains(CatalogValidator.Validate(catalog, source), e => e.Contains("newCommand"));
        Assert.Empty(CatalogValidator.Validate(catalog with { Commands = [..catalog.Commands, Command("newCommand")] }, source));
    }

    [Fact]
    public void Removal_RejectsStaleEntry()
    {
        Assert.Contains(CatalogValidator.Validate(Catalog(), Source.Replace("case \"ping\": break;", "")),
                        e => e.Contains("stale command 'ping'"));
    }

    [Fact]
    public void Metadata_RejectsDuplicatesMissingFieldsAndUnknownVersions()
    {
        var catalog = Catalog();
        var errors = CatalogValidator.Validate(catalog with
        {
            FormatVersion = "2.0",
            Commands = [..catalog.Commands, Command("ping") with { Summary = "", VerificationIds = [] }],
        }, Source);
        Assert.Contains(errors, e => e.Contains("Unsupported catalog format"));
        Assert.Contains(errors, e => e.Contains("Duplicate catalog command"));
        Assert.Contains(errors, e => e.Contains("summary"));
        Assert.Contains(errors, e => e.Contains("verification IDs"));
    }

    [Fact]
    public void TargetAvailability_CannotUseUnionOfAllCommands()
    {
        var catalog = Catalog();
        catalog.Commands[3] = Command("capture");
        Assert.Contains(CatalogValidator.Validate(catalog, Source), e => e.Contains("portable: stale command 'capture'"));
    }

    [Theory]
    [InlineData("switch (method)", "switch (other)")]
    [InlineData("case \"ping\":", "case var name when name.Length > 0:")]
    [InlineData("case \"ping\":", "case CommandName:")]
    [InlineData("switch (method)", "Log(method); switch (method)")]
    [InlineData("default: break;", "")]
    [InlineData("case null:", "case \"ping\":")]
    public void UnknownDispatchShape_FailsClosed(string before, string after)
    {
        Assert.Throws<InvalidDataException>(() => CommandInventory.Read(Source.Replace(before, after), []));
    }

    [Fact]
    public void EmptyOrMissingDispatcher_FailsClosed()
    {
        Assert.Throws<InvalidDataException>(() => CommandInventory.Read("class Server {}", []));
        Assert.Throws<InvalidDataException>(() => CommandInventory.Read("class Server { void ExecuteMethod(string method) { switch(method) { default: break; } } }", []));
    }

    [Fact]
    public void InvalidAliasesAndArguments_AreReported()
    {
        var catalog = Catalog();
        catalog.Commands[0] = Command("ping") with
        {
            AliasOf = "missing",
            Arguments = [new("value", "", false, "", "")],
        };
        var errors = CatalogValidator.Validate(catalog, Source);
        Assert.Contains(errors, e => e.Contains("alias must"));
        Assert.Contains(errors, e => e.Contains("arguments need"));
    }

    [Fact]
    public void MissingRequiredModelFields_AreNotSilentlyDefaulted()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ArgumentContract>(
            """{"name":"value","wireType":"number","description":"Value","omissionBehavior":"error"}""",
            CatalogDocument.JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CatalogDocument>("{}", CatalogDocument.JsonOptions));
    }

    [Fact]
    public void Serialization_PreservesExplicitNullAndRejectsMisspelledFields()
    {
        var catalog = Catalog();
        catalog.Commands[0] = Command("ping") with
        {
            Arguments = [new("value", "any JSON", false, "Opaque codec", "Defaults to null", JsonSerializer.SerializeToElement<object?>(null))],
        };
        var json = JsonSerializer.Serialize(catalog, CatalogDocument.JsonOptions);
        Assert.Empty(CatalogValidator.Validate(JsonSerializer.Deserialize<CatalogDocument>(json, CatalogDocument.JsonOptions)!, Source));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CatalogDocument>(json.Replace("\"summary\"", "\"sumary\""), CatalogDocument.JsonOptions));
    }
}
