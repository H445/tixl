using System.Text.Json;

namespace DebugBridgeCatalog;

internal static class CatalogRepository
{
    internal static CatalogDocument Load(string directory)
    {
        var files = Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
            throw new InvalidDataException("No authored command files found: " + directory);

        var groups = files.Select(file => JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(file), CatalogDocument.JsonOptions)
                                         ?? throw new InvalidDataException("Catalog must be an object: " + file)).ToArray();
        foreach (var group in groups)
        {
            var errors = CatalogValidator.ValidateMetadata(group);
            if (errors.Count != 0)
                throw new InvalidDataException(string.Join("\n", errors));
        }

        var first = groups[0];
        var targets = NormalizeTargets(first.Targets);
        foreach (var group in groups)
        {
            if (group.FormatVersion != first.FormatVersion || group.ProtocolVersion != first.ProtocolVersion
                || JsonSerializer.Serialize(NormalizeTargets(group.Targets)) != JsonSerializer.Serialize(targets))
                throw new InvalidDataException("Authored groups must agree on format, protocol and target definitions.");
        }

        var combined = first with { Targets = targets, Commands = groups.SelectMany(g => g.Commands).OrderBy(c => c.Name, StringComparer.Ordinal).ToArray() };
        var combinedErrors = CatalogValidator.ValidateMetadata(combined);
        if (combinedErrors.Count != 0)
            throw new InvalidDataException(string.Join("\n", combinedErrors));
        return combined;
    }

    internal static CatalogDocument ReadValidated(string root)
    {
        var catalog = Load(Path.Combine(root, "Tools", "DebugBridgeCatalog", "Catalog"));
        var errors = CatalogValidator.Validate(catalog, File.ReadAllText(Path.Combine(root, "Editor", "App", "DebugProtocol", "DebugServer.cs")))
                                     .Concat(VerificationReferences.Validate(catalog, Path.Combine(root, "Tests"))).ToArray();
        if (errors.Length != 0)
            throw new InvalidDataException(string.Join("\n", errors));
        return catalog;
    }

    private static CatalogTarget[] NormalizeTargets(IEnumerable<CatalogTarget> targets)
        => targets.OrderBy(t => t.Name, StringComparer.Ordinal)
                  .Select(t => t with { Symbols = t.Symbols.Order(StringComparer.Ordinal).ToArray() }).ToArray();
}
