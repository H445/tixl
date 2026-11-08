using System.Text.Json;
using System.Text.Json.Serialization;

namespace DebugBridgeCatalog;

internal sealed record CatalogDocument(
    string FormatVersion,
    int ProtocolVersion,
    CatalogTarget[] Targets,
    CommandContract[] Commands)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
    };
}

internal sealed record CatalogTarget(string Name, string[] Symbols);

internal sealed record CommandContract(
    string Name,
    string Summary,
    string[] Targets,
    ArgumentContract[] Arguments,
    string Result,
    string[] Errors,
    string Effects,
    string Completion,
    string[] VerificationIds,
    string? AliasOf = null,
    Dictionary<string, JsonElement>? Extensions = null);

internal sealed record ArgumentContract(
    string Name,
    string WireType,
    bool Required,
    string Description,
    string OmissionBehavior,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Default = default);
