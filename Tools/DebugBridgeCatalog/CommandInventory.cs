using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DebugBridgeCatalog;

internal static class CommandInventory
{
    internal static string[] ConditionalSymbols(string source)
        => CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTrivia(descendIntoTrivia: true)
                          .Select(trivia => trivia.GetStructure())
                          .OfType<DirectiveTriviaSyntax>()
                          .Select(directive => directive switch
                          {
                              IfDirectiveTriviaSyntax condition => condition.Condition,
                              ElifDirectiveTriviaSyntax condition => condition.Condition,
                              _ => null,
                          })
                          .Where(condition => condition != null)
                          .SelectMany(condition => condition!.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                          .Select(identifier => identifier.Identifier.ValueText).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static string[] Read(string source, IEnumerable<string> symbols)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(preprocessorSymbols: symbols));
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidDataException("Cannot parse dispatcher: " + string.Join("; ", errors.Select(e => e.ToString())));
        }

        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                          .Where(m => m.Identifier.ValueText == "ExecuteMethod").ToArray();
        if (methods.Length != 1 || methods[0].Body?.Statements.Count != 1
                                || methods[0].Body!.Statements[0] is not SwitchStatementSyntax dispatch
                                || dispatch.Expression is not IdentifierNameSyntax { Identifier.ValueText: "method" })
        {
            throw new InvalidDataException("Unrecognized ExecuteMethod dispatch shape; update the extractor and its fixtures.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var hasDefault = false;
        foreach (var label in dispatch.Sections.SelectMany(s => s.Labels))
        {
            if (label is DefaultSwitchLabelSyntax)
            {
                hasDefault = true;
                continue;
            }

            if (label is CaseSwitchLabelSyntax { Value: LiteralExpressionSyntax literal })
            {
                if (literal.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    continue;
                }

                if (literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var name = literal.Token.ValueText;
                    if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                    {
                        throw new InvalidDataException($"Empty or duplicate dispatch command '{name}'.");
                    }
                    continue;
                }
            }

            throw new InvalidDataException("Unsupported dispatch label: " + label);
        }

        if (names.Count == 0 || !hasDefault)
        {
            throw new InvalidDataException("Dispatcher must contain literal commands and an unknown-method default.");
        }
        return names.Order(StringComparer.Ordinal).ToArray();
    }
}
