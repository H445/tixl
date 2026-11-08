# Debug Bridge test execution

The legacy `Editor` collection may attach to an existing editor and uses the existing `_agentTests` playground. Do not run the unfiltered project against a user's session. The `VisualSuite` category also modifies saved example state. New contract tests use the `OwnedEditor` collection instead.

## Offline checks

```powershell
dotnet test Tests/Editor.IntegrationTests/Editor.IntegrationTests.csproj --filter Category=Offline
dotnet test Tests/DebugBridgeCatalog.Tests/DebugBridgeCatalog.Tests.csproj
```

Offline client tests use loopback fake servers. They do not initialize an editor fixture. Strict response mode is opt-in on the existing client, validates ID and envelope fields, preserves raw JSON, and closes after a failed exchange without retrying. Legacy convenience methods remain available.

## Owned live sessions

Select an explicit installed/built editor and backend on an interactive graphics-capable runner. This launches and later shuts down a new editor. It never attaches to the default port or searches for an existing project.

```powershell
$env:TIXL_CONTRACT_EXE = 'C:\path\to\TiXL.exe'
$env:TIXL_CONTRACT_BACKEND = 'd3d11' # Windows only; use 'vulkan' for Vulkan
dotnet test Tests/Editor.IntegrationTests/Editor.IntegrationTests.csproj --filter Category=OwnedEditor --logger 'console;verbosity=normal'
```

On Linux/macOS set the same variables in that platform's shell and select `vulkan`. Their path calculations are based on the existing `FileLocations` rules, but need live verification on those platforms before claiming support.

The fixture passes a fresh `--override-version-id`, seeds `userSettings.json` with only its own project root, uses an ephemeral loopback port, and verifies all four `getPaths` roots and the log directory before submitting a mutation. A port collision cannot authorize an unrelated editor: the unique profile path must match. The fixture then calls existing `newProject` and `openProject` and exposes their home symbol ID for tests. Put extra test assets in this disposable project, never in shipped packages.

On Windows the fixture refuses a binary whose directory differs from the user's existing `T3_ASSEMBLY_PATH`, because editor startup would change that shared setting. Use an appropriately configured dedicated test account for a different editor build. The fixture does not change that setting to make a test pass. Explicit capture/export paths in future tests must stay under the session's temp/project roots; devices and other global state need separately established isolation.

The fixture captures version, requested backend, process ID, owned paths and editor logs under the system temp directory's `tixl-contract-evidence/<run-id>/`. Logs establish which backend actually initialized; the selected environment variable alone is not runtime proof. A successful teardown writes `cleanup.txt` after shutting down its owned process and removing only marked roots. Cleanup rejects changed ownership and reparse points, including links inside a tree. Failed startup retains the marked folders and failure evidence for diagnosis, and stops only the launched process. Inspect those exact paths before any later manual cleanup.

Missing executable/backend configuration fails explicitly; it is not a passing skip. A successful installed-editor smoke test validates the harness and that artifact only. It does not prove the current source build's contracts, all commands, or other platforms.

## Extending coverage

Use [DebugBridgeCoverage.md](DebugBridgeCoverage.md) to locate reusable assertions and work assignments. Add new offline tests without a live collection, and new live contract classes with both `[Collection("OwnedEditor")]` and `[Trait("Category", "OwnedEditor")]`. Use `fixture.Editor.Client` and `fixture.Editor.OwnedSession` rather than constructing another transport or scanning user folders. Keep expectations independent of authored command metadata. Record actual test results and known limitations rather than treating a verification ID as evidence of execution.
