using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DebugBridgeCatalog;

internal static class VerificationReferences
{
    internal static IReadOnlyList<string> Validate(CatalogDocument catalog, string testsDirectory)
    {
        var definitions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(testsDirectory))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!method.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
                        a.Name.ToString().Split('.').Last() is "Fact" or "FactAttribute" or "Theory" or "TheoryAttribute"))
                    continue;
                var ancestors = method.Ancestors().Reverse().Select(node => node switch
                {
                    BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
                    TypeDeclarationSyntax type => type.Identifier.ValueText,
                    _ => null,
                }).Where(part => part != null);
                var name = string.Join(".", ancestors.Append(method.Identifier.ValueText));
                definitions[name] = definitions.GetValueOrDefault(name) + 1;
            }
        }
        return catalog.Commands.SelectMany(command => command.VerificationIds.Select(id => (command.Name, Id: id)))
                      .Where(reference => definitions.GetValueOrDefault(reference.Id) != 1)
                      .Select(reference => $"{reference.Name}: verification '{reference.Id}' must resolve to exactly one xUnit Fact/Theory source method.")
                      .ToArray();
    }

    private static IEnumerable<string> SourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs").Order(StringComparer.Ordinal))
            yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(child) is "bin" or "obj" || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                continue;
            foreach (var file in SourceFiles(child))
                yield return file;
        }
    }
}
