# Debug Bridge contract coverage

Baseline source review: `b6cc58440`. No live test execution is implied by this inventory.
The dispatcher is authoritative; the 50 names below are a snapshot, not an expected constant.
All commands are currently unconditional switch cases; backend/OS availability may still differ inside handlers.

## Target matrix

| Target | Dispatch inventory | Required runtime evidence |
| --- | --- | --- |
| Windows / D3D11 | Current source has the same callable set | Owned interactive editor; GPU, capture, render, audio prerequisites recorded per test |
| Windows / Vulkan | Same names; backend behavior needs verification | Owned Vulkan session; no parity inferred from shared dispatch |
| Linux / Vulkan | Same source names; build support must be checked for release | Owned interactive session; platform/device outcomes |
| macOS / Vulkan | Same source names; build support must be checked for release | Owned interactive session; platform/device outcomes |

The repository's cross-platform policy defines these targets; this source audit does not establish that this checkout builds or passes on each.
`screenshotWindow` is Windows-specific. GPU metrics, codecs, devices, minimized/modal states and unavailable resources need explicit outcomes.
Do not auto-run the existing unfiltered integration suite: its fixture can attach to an existing editor and its visual suite modifies saved package state.

## Command assignments

The `bridge.*` IDs below identify work assignments from the initial audit. Authored catalog entries use fully qualified test method names as their verification IDs. Existing assertions must be reused and strengthened where sufficient.
“Missing” means no direct contract assertion was found in the four reviewed test files.
Runtime evidence is pending unless an actual run is recorded in the ledger below.

| Command | Sprint item | Verification ID | Existing evidence from source review |
| --- | --- | --- | --- |
| `ping` | DB-05 | `bridge.ping` | ProtocolBasicsTests.Ping_ReturnsEnvelopeWithFrameAndStructureVersion |
| `setAgentState` | DB-05 | `bridge.setAgentState` | Missing |
| `getVersion` | DB-05 | `bridge.getVersion` | ProtocolBasicsTests.GetVersion_ReportsProtocolVersion |
| `getPaths` | DB-05 | `bridge.getPaths` | Missing |
| `shutdown` | DB-05 | `bridge.shutdown` | Used by existing workflows; no independent full-contract assertion |
| `getStructureVersion` | DB-05 | `bridge.getStructureVersion` | Missing |
| `getLogTail` | DB-05 | `bridge.getLogTail` | ProtocolBasicsTests.GetLogTail_ReturnsSequencedEntries |
| `getContext` | DB-05 | `bridge.getContext` | Missing |
| `getMetrics` | DB-05 | `bridge.getMetrics` | ProtocolBasicsTests.GetMetrics_ReportsGpuMemoryAndFrameTiming |
| `getShaderErrors` | DB-05 | `bridge.getShaderErrors` | Missing |
| `getFrameSamples` | DB-05 | `bridge.getFrameSamples` | Missing |
| `stallMainThread` | DB-05 | `bridge.stallMainThread` | Missing |
| `setVSync` | DB-05 | `bridge.setVSync` | Missing |
| `releaseGpuResources` | DB-05 | `bridge.releaseGpuResources` | Missing |
| `setTargetFrameRate` | DB-05 | `bridge.setTargetFrameRate` | Missing |
| `setFrameLatency` | DB-05 | `bridge.setFrameLatency` | Missing |
| `getGraphState` | DB-06 | `bridge.getGraphState` | GraphAcceptanceTests.BuildRenderRecolorUndo_RoundTrips (graph counts/connections) |
| `select` | DB-06 | `bridge.select` | Used by existing workflows; no independent full-contract assertion |
| `setInput` | DB-06 | `bridge.setInput` | GraphAcceptanceTests.SetInput_ByName_IsCaseInsensitive; BuildRenderRecolorUndo_RoundTrips |
| `getOutput` | DB-06 | `bridge.getOutput` | Used by existing workflows; no independent full-contract assertion |
| `addOp` | DB-06 | `bridge.addOp` | GraphAcceptanceTests.AddOp_UnknownSymbol_ReturnsNotFound; BuildRenderRecolorUndo_RoundTrips |
| `connect` | DB-06 | `bridge.connect` | GraphAcceptanceTests.BuildRenderRecolorUndo_RoundTrips (connection present) |
| `deleteOp` | DB-06 | `bridge.deleteOp` | Missing |
| `setBypass` | DB-06 | `bridge.setBypass` | Missing |
| `deleteSelection` | DB-06 | `bridge.deleteSelection` | Missing |
| `undo` | DB-06 | `bridge.undo` | GraphAcceptanceTests.BuildRenderRecolorUndo_RoundTrips (restored counts) |
| `redo` | DB-06 | `bridge.redo` | Missing |
| `openProject` | DB-07 | `bridge.openProject` | Used by existing workflows; no independent full-contract assertion |
| `newProject` | DB-07 | `bridge.newProject` | Missing |
| `reload` | DB-07 | `bridge.reload` | ReloadTests.Reload_BrokenSourceReportsErrors_FixedSourceSucceeds; Reload_UnknownProject_ReturnsNotFound |
| `openWindow` | DB-08 | `bridge.openWindow` | Missing |
| `getGraphView` | DB-08 | `bridge.getGraphView` | Missing |
| `setGraphView` | DB-08 | `bridge.setGraphView` | Missing |
| `focusGraphView` | DB-08 | `bridge.focusGraphView` | Missing |
| `pin` | DB-08 | `bridge.pin` | Used by existing workflows; no independent full-contract assertion |
| `outputSetup` | DB-08 | `bridge.outputSetup` | Missing |
| `resetView` | DB-08 | `bridge.resetView` | Used by existing workflows; no independent full-contract assertion |
| `layoutSelection` | DB-08 | `bridge.layoutSelection` | Missing |
| `screenshot` | DB-09 | `bridge.screenshot` | GraphAcceptanceTests.BuildRenderRecolorUndo_RoundTrips (file size/image changed) |
| `screenshotWindow` | DB-09 | `bridge.screenshotWindow` | Missing |
| `startRender` | DB-09 | `bridge.startRender` | Missing |
| `getRenderStatus` | DB-09 | `bridge.getRenderStatus` | Missing |
| `cancelRender` | DB-09 | `bridge.cancelRender` | Missing |
| `getAudioInputs` | DB-10 | `bridge.getAudioInputs` | Missing |
| `setAudioInput` | DB-10 | `bridge.setAudioInput` | Missing |
| `startAudioRecording` | DB-10 | `bridge.startAudioRecording` | Missing |
| `stopAudioRecording` | DB-10 | `bridge.stopAudioRecording` | Missing |
| `pumpFrames` | DB-10 | `bridge.pumpFrames` | Used by existing workflows; no independent full-contract assertion |
| `setTime` | DB-10 | `bridge.setTime` | Missing |
| `setPlayback` | DB-10 | `bridge.setPlayback` | Missing |

## Existing tests to reuse

- `ProtocolBasicsTests` asserts selected envelope fields, protocol version, metrics/log shapes, and the unknown-method error. It does not check ID echo, raw error shape, playbackFrame, or deferred reply correlation.
- `GraphAcceptanceTests` checks a build/render/recolor/undo workflow and two error/name-lookup cases. The case-insensitive input test only requires success; add an actual changed-value assertion.
- `ReloadTests` checks compile failure/recovery and unknown project. Its source-path search and shared playground must not be reused by owned-session tests.
- `VisualSuiteTests` exercises graph discovery, selection, trigger input, frame pumping, and output polling. It is a broad visual regression workflow with known flaky cases, not an independent contract oracle. It modifies shipped example trigger state and is excluded from the new owned contract collection.

## Behavior classes and verification gaps

| Class | Planned verification | Prerequisites / boundaries |
| --- | --- | --- |
| Input codecs | `bridge.setInput`: literal/opaque values, omission/null, bounds, enums, vectors/matrices, collections and assets | Representative existing operators or test-only fixtures; no library scanner |
| Output variants | `bridge.getOutput`: scalar/vector, unsupported null, summaries, cached/forced evaluation, list/buffer ranges and mesh export | Explicit GPU/file work; same-stride non-point fixture |
| Identity and graph | `bridge.getGraphState`, `bridge.connect`, `bridge.addOp`: default inputs, GUID/name precedence, missing/ambiguous symbols, cycles and reload | Existing registry stays authoritative; no probing by mutation |
| Wire/lifecycle | `bridge.wire`: malformed JSON/types, IDs, errors, slow readers, competing clients, disconnect/restart and timeouts | Offline fake server first; disruptive live cases in fresh owned sessions |
| Completion/effects | `bridge.screenshot`, `bridge.startRender`, `bridge.pumpFrames`: accepted versus complete, file readability, late error | Device/platform evidence; no automatic replay after unknown outcome |

## Known limitations requiring disposition

- Screenshot saving can log a failure but complete with a filename; UI capture has one pending callback which can be replaced. Do not promise file-saved or concurrent-capture guarantees.
- `addOp` by unknown symbol GUID can report a child ID without adding a child. Assert graph postconditions.
- `getOutput` treats an unparseable child ID like an omitted ID and reads the root.
- Method conversion occurs outside the handler catch; malformed method tokens need a controlled test.
- Stopping the bridge does not visibly clear queued requests or deferred frame waits. Disconnect is not cancellation.
- Integration test isolation and strict response validation are prerequisites, not currently proven by the legacy fixture.

Each limitation needs a reproducible case and recorded current outcome. Production fixes remain separate changes.
Manual/device evidence must name target/backend, case, actual result and artifact location. A missing run is not a passing skip.

## Evidence ledger

| Check | Result | Evidence |
| --- | --- | --- |
| Source inventory and work assignment | Reviewed | 50 unique dispatcher names; every name mapped once above |
| Existing test review | Reviewed | Four test classes listed above; source review only |
| Offline catalog foundation and authored groups | Passed | 19 tests, including omitted/null defaults and authored-group validation; full exact-set completeness remains a separate gate |
| Offline strict client, owned paths and log rollover | Passed | 23 tests; production log buffer linked only into the test project; no live fixture initialized |
| Owned harness, Windows / D3D11 | Passed on installed 4.4.0.4 | Run `218e545b3b96494ea1989e96fe571988`; logs confirm Direct3D 11; cleanup verified |
| Owned harness, Windows / Vulkan | Passed on installed 4.4.0.4 | Run `2145bc19649244d0a2fabd668c1bb055`; logs confirm Vulkan 1.4.341; cleanup verified |
| Source Release build | Passed on Windows | `dotnet build t3.sln -c Release`; zero warnings/errors; production source at `bf4b95c3d` unchanged by this feature; build hashes recorded with live results |
| Session/diagnostics source contracts | Passed on Windows D3D11 and configured Vulkan | Nine cases per backend in `tixl-reviewed-contracts-0ed2c3655c504d709db85e83b1e89b88`, plus resource release/rebuild in `tixl-contract-followup-f9fc4bf142d54a038dc9468ce64c604c`; 10 session cases total per backend |
| Graph/slot initial source contracts | Passed on Windows D3D11 and configured Vulkan | Seven unchanged cases per backend in the main run, corrected connected-bypass case and added partial-bypass case in the targeted follow-up; nine cases total per backend |
| Project lifecycle initial source contracts | Passed on Windows D3D11 and configured Vulkan | Three cases per backend in `tixl-reviewed-contracts-0ed2c3655c504d709db85e83b1e89b88`; owned paths, open selectors and compile failure/recovery with identity/history assertions |
| Full live command contract matrix | Pending | Partial groups are not a full dispatcher or operator behavior-class pass |
| Linux/macOS live matrix | Pending | Path/launch code reviewed; hardware runs remain required |
| Runtime dependency/payload comparison | Pending | Required before first release |

Smoke evidence is local under the system temp directory's `tixl-contract-evidence/<run-id>/`, including session provenance, editor log and cleanup confirmation. Both runs created and inspected a disposable project through existing commands, terminated their owned editor, and removed settings/cache/projects/temp roots. The installed editor is not claimed to match the source baseline. No production code, operator package or editor project references changed during the foundation work.

Future changes update this record together with metadata/tests. Keep detailed logs and disposable projects outside tracked source.

## Session/diagnostic contract checkpoint

`Catalog/session.json` describes 16 commands. `SessionContractTests` independently checks no-project diagnostics, owned-project context, agent-state argument handling, live log filtering/cursors, metric/sample shapes, bounded stalls, setting clamps and persistence, shader failures/source access/clear, and shutdown. `DebugLogBufferTests` exercises the actual ring buffer's 4096-entry rollover and window-before-filter behavior without loading the editor. Expectations do not read authored metadata.

Source-build results are under the system temp directory's `tixl-reviewed-contracts-0ed2c3655c504d709db85e83b1e89b88/`, with TRX files and a build manifest containing source commit, dispatcher hash and editor DLL hash. The source build reports 4.4.0.5-alpha; an initial installed 4.4.0.4 run was insufficient because it lacks `getFrameSamples`. Installed results are not substituted for current-source evidence.

Behavior corrected through live evidence: omitted setting values report current state but explicit JSON null returns `INTERNAL_ERROR`; frame/CPU/presentation sample arrays are independent and can have different warmup lengths. Shader tests deliberately compile two invalid shaders inside the owned project, check newest-first errors, read captured source, remove only an owned captured file to verify `sourceError`, then clear the failures.

The targeted resource case passed on both backends in `tixl-contract-followup-f9fc4bf142d54a038dc9468ce64c604c`. It evaluates an unpinned 64-by-32 render target, requests release, advances frames, verifies the cached texture becomes null and the release is logged, then forces evaluation and compares the rebuilt texture descriptor. This checks the effect rather than treating the acknowledgement or a fluctuating global memory counter as proof of release.

The first source-build Vulkan startup failed before dispatch because in-process Slang 2026.18 could not load `spirv-opt`. Standalone shader compilation succeeded; adding the compiler's `bin` folder to only the runner process's PATH made the owned startup probe pass (`tixl-vulkan-path-probe-8a95a8a90ddd4b03a7d335329854114b`). Subsequent Vulkan results use that explicit prerequisite. Default source startup remains a separate production issue. The failed run is retained as failed/aborted in `tixl-contract-groups-82e4adec3e7d4b58a6b16750c432a246`, with owned startup evidence retained for diagnosis.

The user authorized temporarily setting `T3_ASSEMBLY_PATH` to the source build for these runs. Launch wrappers check for other TiXL processes, preserve the original user/process values, and restore/verify them in `finally`; the fixture guard remains unchanged. No permanent PATH setting was changed. These local runs are not a general permission for CI to alter shared account state.

Remaining session evidence includes UI-only ready-to-busy transitions, unavailable-device metric branches, extreme-duration stall behavior, and Linux/macOS runs. Tests establish observed contracts; they do not establish every device state or a latency guarantee. Full release gates remain open.

## Graph/slot contract checkpoint

`Catalog/graph.json` adds 11 commands. Nine `GraphContractTests` cases exercise known-symbol inspection, default-value inclusion, scalar/vector/string codecs, GUID/name precedence, malformed and unknown IDs, cached versus forced output evaluation, type mismatch and cycle rejection, connections restored by undo, selection/deletion/history and bypass. Both Windows backend configurations pass across the main and targeted run directories above. All 40 main-run and six targeted-run sessions have cleanup records; the main run's single graph failure per backend is retained as superseded evidence, not relabeled as passed.

Reproducible existing limitations have explicit assertions: an unknown symbol GUID can return a child ID without adding a child; a malformed output child ID selects the root; an instantiated node with no output connection can ignore bypass. A further bypass defect was discovered: with the output connected but its scalar first input unconnected, `Instance.SetBypassFor` changes the output action, then invalidation dereferences the absent input connection. The command catches the exception and reports success with stored bypass still false, while evaluated output changes. The targeted test verifies the false stored flag, changed numeric result and error log. The normal bypass/undo test uses connections on both sides and verifies the intended result. No production fix is included.

DB-06 remains open for remaining behavior classes and cases, including name ambiguity, placement defaults, multi-input ordering, missing symbols/connections, collection/buffer ranges, mesh export, opaque/null inputs, broader numeric/enum/matrix codecs, no-op history boundaries and cross-platform runs. Texture release/rebuild and unsupported shader output null are covered by the session tests; they do not establish every output variant. Operator discovery continues to use existing graph/context calls; no library index or runtime reflection was added.

## Project lifecycle contract checkpoint

`Catalog/projects.json` adds `newProject`, `openProject` and `reload`. Three `ProjectContractTests` cases pass on both configured Windows backends in the main run above. They reuse the fixture's project provisioning and the existing broken-source/recovery assertion pattern, restricted to the owned project's source. The checks cover GUID precedence, case-insensitive short names, missing/unknown selectors, built-in package reload rejection, compile failure diagnostics, recovery, preserved child IDs and output values, and undo/redo after reloading instances. The fixture verifies all owned roots and cleanup.

DB-07 remains open for creation failures/collisions, malformed or missing package state, explicit open-failure and absent-window branches, and Linux/macOS execution. Names are passed to existing project setup, which writes files; automation must supply unique names and controlled roots. The bridge is not a filesystem sandbox, and a failed create can leave files. No broad search of user projects or modification of shipped operator packages is part of these tests.
