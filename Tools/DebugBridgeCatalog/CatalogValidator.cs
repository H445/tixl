namespace DebugBridgeCatalog;

internal static class CatalogValidator
{
    internal static IReadOnlyList<string> Validate(CatalogDocument catalog, string source)
    {
        var errors = new List<string>();
        if (catalog.FormatVersion != "1.0")
        {
            errors.Add($"Unsupported catalog format '{catalog.FormatVersion}'; supported format is 1.0.");
        }
        if (catalog.ProtocolVersion < 1)
        {
            errors.Add("A positive bridge protocol version is required.");
        }
        if (catalog.Targets == null || catalog.Targets.Length == 0 || catalog.Commands == null)
        {
            errors.Add("Targets and commands must be supplied.");
            return errors;
        }

        var targetNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in catalog.Targets)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.Name) || !targetNames.Add(target.Name)
                               || target.Symbols == null || target.Symbols.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add("Targets must have unique names and explicit conditional symbols (an empty array is allowed).");
            }
        }

        var commandNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in catalog.Commands)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Name))
            {
                errors.Add("Every command requires a name.");
                continue;
            }
            if (!commandNames.Add(command.Name))
            {
                errors.Add($"Duplicate catalog command '{command.Name}'.");
            }
            if (string.IsNullOrWhiteSpace(command.Summary) || string.IsNullOrWhiteSpace(command.Result)
                || string.IsNullOrWhiteSpace(command.Effects) || string.IsNullOrWhiteSpace(command.Completion))
            {
                errors.Add($"{command.Name}: summary, result, effects and completion are required.");
            }
            if (command.Targets == null || command.Targets.Length == 0
                                        || command.Targets.Any(t => !targetNames.Contains(t)))
            {
                errors.Add($"{command.Name}: declare valid target availability.");
            }
            if (command.VerificationIds == null || command.VerificationIds.Length == 0
                                                || command.VerificationIds.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"{command.Name}: verification IDs are required (references must also be checked before release).");
            }
            if (command.Errors == null || command.Errors.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"{command.Name}: errors must be explicit (an empty array is allowed).");
            }
            if (command.Arguments == null)
            {
                errors.Add($"{command.Name}: arguments must be explicit (an empty array is allowed).");
                continue;
            }
            var argumentNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var argument in command.Arguments)
            {
                if (argument == null || string.IsNullOrWhiteSpace(argument.Name) || !argumentNames.Add(argument.Name)
                                     || string.IsNullOrWhiteSpace(argument.WireType)
                                     || string.IsNullOrWhiteSpace(argument.Description)
                                     || string.IsNullOrWhiteSpace(argument.OmissionBehavior))
                {
                    errors.Add($"{command.Name}: arguments need unique names, wire type, description and omission behavior.");
                }
            }
        }

        foreach (var command in catalog.Commands.Where(c => c != null && c.AliasOf != null))
        {
            var original = catalog.Commands.FirstOrDefault(c => c?.Name == command.AliasOf);
            if (original == null || original.AliasOf != null || original.Name == command.Name
                                 || command.Targets == null || original.Targets == null
                                 || command.Targets.Except(original.Targets, StringComparer.Ordinal).Any())
            {
                errors.Add($"{command.Name}: alias must identify a non-alias command available on the same targets.");
            }
        }

        foreach (var target in catalog.Targets.Where(t => t != null && t.Symbols != null))
        {
            try
            {
                var actual = CommandInventory.Read(source, target.Symbols);
                var documented = catalog.Commands.Where(c => c?.Targets?.Contains(target.Name, StringComparer.Ordinal) == true)
                                        .Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var name in actual.Except(documented, StringComparer.Ordinal))
                {
                    errors.Add($"{target.Name}: missing command '{name}'; add reviewed metadata and independent verification.");
                }
                foreach (var name in documented.Except(actual, StringComparer.Ordinal))
                {
                    errors.Add($"{target.Name}: stale command '{name}'; remove it or correct target availability.");
                }
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException)
            {
                errors.Add($"{target.Name}: {e.Message}");
            }
        }
        return errors;
    }
}
