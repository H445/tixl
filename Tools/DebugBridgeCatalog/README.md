# Offline Debug Bridge catalog tooling

This tool reads source and reviewed metadata. It never loads Editor/Core or connects to TiXL.
Run from the repository root with the repository's .NET SDK:

```powershell
dotnet run --project Tools/DebugBridgeCatalog -- inventory Editor/App/DebugProtocol/DebugServer.cs
dotnet run --project Tools/DebugBridgeCatalog -- validate Editor/App/DebugProtocol/DebugServer.cs path/to/catalog.json
dotnet test Tests/DebugBridgeCatalog.Tests/DebugBridgeCatalog.Tests.csproj
```

`inventory` optionally takes explicit conditional symbols after the source path. `validate` checks every declared catalog target against its own parsed callable set and exits 1 on failure. Invalid invocation exits 2. The extractor intentionally fails on unfamiliar dispatch structure rather than reporting a partial inventory.

## Initial format (1.0)

The document has `formatVersion`, `protocolVersion`, `targets` (name and conditional symbols), and `commands`. Each command supplies name, summary, target names, arguments, result description, error codes, effects, completion, and verification IDs. Optional `aliasOf` points directly to a canonical entry; optional `extensions` holds additive descriptive metadata. Each argument supplies name, wire type description, required flag, description, omission behavior, and optional JSON default. Omission rules are explicit and do not cause the tool to insert defaults into requests.

Validation is for the catalog model, not a general JSON Schema request validator. Wire types, result shapes and conditional behavior are reviewed descriptions; no automatic HTTP routing or input validation is implied. Unknown model properties are rejected to catch spelling errors; additive experimental metadata belongs in `extensions`. Unsupported catalog versions fail explicitly.

Format review considered added commands/aliases, target-specific availability, slot-dependent codecs, richer future result schemas, and deprecation annotations. Descriptive extensions can grow without executing code. Structured schemas or required new semantics need an explicit versioned format change and consumer tests; unsupported semantics must never be guessed.

The complete reviewed command metadata, renderer, `generate`/`check`, verification-reference resolution, and CI integration are subsequent work. Test fixtures are synthetic and are not production documentation. This foundation validates metadata references are present; it does not prove that their tests exist or passed. No complete command catalog is published until full coverage and those gates are implemented.

Keep deterministic contract data separate from the release provenance manifest. Do not embed current HEAD or timestamps in tracked generated output. Tests and explicit CLI calls run independently of editor builds; no runtime registration or operator scanning is added.
