using System.Text.Json;

namespace DebugBridgeCatalog;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
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
            Console.Error.WriteLine("Usage: inventory <DebugServer.cs> [conditional symbols...] | validate <DebugServer.cs> <catalog.json>");
            return 2;
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
}
