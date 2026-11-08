using System.Text.Json;

namespace DebugBridgeCatalog;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length is 1 or 3 && args[0] is "generate" or "check"
                && (args.Length == 1 || args[1] == "--repo"))
            {
                var root = Path.GetFullPath(args.Length == 3 ? args[2] : Directory.GetCurrentDirectory());
                var catalog = CatalogRepository.ReadValidated(root);
                var output = CatalogOutput.Render(catalog);
                if (args[0] == "generate")
                    CatalogOutput.Write(root, output);
                var errors = CatalogOutput.Check(root, output);
                foreach (var error in errors)
                    Console.Error.WriteLine(error);
                if (errors.Count == 0)
                    Console.WriteLine($"{catalog.Commands.Length} commands validated; generated Markdown and JSON are current. Live behavior evidence is separate.");
                return errors.Count == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "inventory")
            {
                var commands = CommandInventory.Read(File.ReadAllText(args[1]), args.Skip(2));
                Console.WriteLine(JsonSerializer.Serialize(commands, CatalogDocument.JsonOptions));
                return 0;
            }
            if (args.Length == 3 && args[0] == "validate")
            {
                var catalog = JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(args[2]), CatalogDocument.JsonOptions)
                              ?? throw new InvalidDataException("Catalog must be an object.");
                var errors = CatalogValidator.Validate(catalog, File.ReadAllText(args[1]));
                foreach (var error in errors)
                {
                    Console.Error.WriteLine(error);
                }
                if (errors.Count == 0)
                {
                    Console.WriteLine("Catalog metadata and target-scoped dispatch sets are valid.");
                }
                return errors.Count == 0 ? 0 : 1;
            }
            Console.Error.WriteLine("Usage: generate|check [--repo <root>] | inventory <DebugServer.cs> [conditional symbols...] | validate <DebugServer.cs> <catalog.json>");
            return 2;
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
}
