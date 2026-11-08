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

Verification IDs identify planned contract coverage, not existing tests. Existing assertions must be reused and strengthened where sufficient.
“Missing” means no direct contract assertion was found in the four reviewed test files.
All runtime evidence is pending until an actual run is recorded.

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
| Offline catalog foundation | Passed | 18 tests, including omitted versus explicit-null defaults; real Roslyn extraction matches all 50 source commands |
| Offline strict client and owned-path checks | Passed | 21 tests; no live fixture initialized |
| Owned harness, Windows / D3D11 | Passed on installed 4.4.0.4 | Run `218e545b3b96494ea1989e96fe571988`; logs confirm Direct3D 11; cleanup verified |
| Owned harness, Windows / Vulkan | Passed on installed 4.4.0.4 | Run `2145bc19649244d0a2fabd668c1bb055`; logs confirm Vulkan 1.4.341; cleanup verified |
| Full live command contracts and source-build match | Pending | Smoke results establish harness operation only; current checkout was not built or tested live |
| Linux/macOS live matrix | Pending | Path/launch code reviewed; hardware runs remain required |
| Runtime dependency/payload comparison | Pending | Required before first release |

Smoke evidence is local under the system temp directory's `tixl-contract-evidence/<run-id>/`, including session provenance, editor log and cleanup confirmation. Both runs created and inspected a disposable project through existing commands, terminated their owned editor, and removed settings/cache/projects/temp roots. The installed editor is not claimed to match the source baseline. No production code, operator package or editor project references changed during the foundation work.

Future changes update this record together with metadata/tests. Keep detailed logs and disposable projects outside tracked source.
