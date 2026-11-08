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
| Window/view initial source contracts | Passed on Windows D3D11 and configured Vulkan | Four unchanged cases in `tixl-view-contracts-6638e94adb4548a5be2bd4205bf98fd6`, plus stale-view/refresh characterization in `tixl-contract-run-cf0ed0f6a2404758ad5464fbcfe5885a`; five cases per backend |
| Capture/render initial source contracts | Passed on Windows D3D11 and configured Vulkan | Seven cases in `tixl-contract-run-8d3a511367cb4d64beb25be471ffd037`, plus overlapping capture and strengthened video decode in `tixl-contract-run-92d141dc6e1b4a418a1b411d7e0c0439`; eight distinct cases per backend |
| Playback/wire and non-capturing audio source contracts | Passed on Windows D3D11 and configured Vulkan | Nine cases per backend in `tixl-contract-run-402cd298f6da49b6abaefa1c315f5c42`; 18 owned cleanups verified; no real input capture requested |
| Operator output classes and multi-input ordering | Passed on Windows D3D11 and configured Vulkan | Four D3D11 cases in `tixl-contract-run-6fb1f51d92594debbcd162277fd982b8`, corrected matrix plus ordering in `tixl-contract-run-0db3683d662e41ba9b13ef5e1b768350`, all six Vulkan cases in `tixl-contract-run-386629a934bb4421836456789ef993e9` |
| Complete authored command set | Passed offline | Combined metadata exactly matches source dispatch for each declared target; 20 catalog tests pass. This checks command names, not complete behavior coverage |
| Generated reference and JSON | Passed offline | 28 catalog tests; all verification IDs resolve to source Fact/Theory methods. Two real generations with TiXL closed produced identical bytes; `check` passes. Missing/stale/obsolete output, new-command metadata/reference gaps and unknown conditional symbols are rejected |
| Full live command contract matrix | Pending | Initial groups are not a complete operator behavior-class or platform pass; recording prerequisites remain open |
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

Six additional `OutputContractTests` cases exercise integer/bool/vector outputs and trigger flanks, collection-wrapper and matrix inputs, opaque matrix/list output null, matching CPU Point list and GPU buffer readback, read ranges, generic integer-buffer reinterpretation as float values, empty/null buffers, multi-input insertion order through delete/undo, and cube geometry bounds/volume/topology with readable OBJ export and path failure. They reuse existing operators in owned projects. The initial matrix assertion compared integer and float JSON token types; the corrected assertion checks all 16 numeric values. The original failed run remains failed evidence, superseded by the targeted pass. Six distinct cases now pass per backend; all 13 sessions across the initial/targeted/Vulkan runs were cleaned up.

The catalog now spells out actual summary fields and codec limitations. Ordinary List<int> and Matrix4x4 outputs are opaque null despite readable stored inputs; malformed matrix shape silently selects identity. Buffer readback is a representation heuristic: 64-byte strides become Point-shaped JSON, other strides become float arrays even when the underlying data is integer. Neither shape proves a semantic element type. Tests compare known integer bit patterns to expected floats, and CPU/GPU points to independently chosen coordinates.

DB-06 remains open for remaining behavior classes and cases, including name ambiguity, placement defaults, missing symbols/connections, same-stride non-point and unusual-stride buffers, opaque/null input variants, broader numeric/enum/flags/custom codecs, nested/addressed compositions, no-output/multiple-output and no-op history boundaries, async evaluation failures and cross-platform runs. Texture release/rebuild and unsupported shader output null are covered by the session tests; they do not establish every output variant. Operator discovery continues to use existing graph/context calls; no library index or runtime reflection was added.

## Project lifecycle contract checkpoint

`Catalog/projects.json` adds `newProject`, `openProject` and `reload`. Three `ProjectContractTests` cases pass on both configured Windows backends in the main run above. They reuse the fixture's project provisioning and the existing broken-source/recovery assertion pattern, restricted to the owned project's source. The checks cover GUID precedence, case-insensitive short names, missing/unknown selectors, built-in package reload rejection, compile failure diagnostics, recovery, preserved child IDs and output values, and undo/redo after reloading instances. The fixture verifies all owned roots and cleanup.

DB-07 remains open for creation failures/collisions, malformed or missing package state, explicit open-failure and absent-window branches, and Linux/macOS execution. Names are passed to existing project setup, which writes files; automation must supply unique names and controlled roots. The bridge is not a filesystem sandbox, and a failed create can leave files. No broad search of user projects or modification of shipped operator packages is part of these tests.

## Window/view contract checkpoint

`Catalog/views.json` adds eight commands. Five `ViewContractTests` cases pass per configured Windows backend across the view and targeted runs above: no-composition errors, window/section lookup, pin identity through `getContext`, absolute/relative/smooth target scopes, scale/area validation, deferred fitting, explicit-child focus and selection clearing, and graph layout/undo. The two initial layout failures remain failed evidence; the corrected case characterizes existing behavior rather than claiming it works without a prerequisite.

Bridge add/connect changes can leave the graph UI layout cache stale even while `getGraphState` reports the new children/connections. Framing the nodes and advancing frames did not repair it; a captured UI was blank in the requested area. Source inspection found that graph layout rebuilds on explicit change flags, undo/redo or changed composition identity, while these handlers do not notify the view. The test verifies `changed:false` and unchanged selection, then undoes/redoes only its own last connection, allowing the existing refresh path to run. Layout then moves the upstream node, expands selection, preserves connections, and its undo restores the original position. This is a constrained reproducible workaround, not advice to replay arbitrary agent edits.

DB-08 remains open for active output-setup entity/mode visual verification, missing-output-window branches, numeric-enum/invalid-geometry behavior beyond the tested bounds, missing-symbol focus, active interactions/modal/minimized states, and Linux/macOS execution. Reset acknowledgement is tested; camera appearance is still a manual visual prerequisite. No native panel, window registration, UI hook or runtime discovery API was added.

## Capture/render contract checkpoint

`Catalog/capture.json` adds five commands. Eight distinct `CaptureContractTests` cases pass on both configured Windows backends across the capture runs above. PNG/JPEG files are decoded with test-only StbImageSharp 2.30.16 (the same version already used by Core); dimensions and red output pixels are checked when replies arrive. The cases cover `.png`, `.JPG`, `.jpeg` (PNG), spaces/Unicode paths, missing output, UI opaque alpha, Windows graph-region capture, parent-folder creation and explicit window-save errors. Artifacts are copied from the owned temp root into evidence before session cleanup.

Known screenshot defects are reproduced: a missing parent folder can yield a successful path reply while the save logs a warning and produces no file. Pipelined UI captures overwrite the one pending callback: a bounded stall allows both requests to queue, the later image completes, the earlier file/reply is absent, and an independent client remains responsive. The test treats the missing response as an unknown outcome and never replays it. `OwnedWireConnection` is a thin raw-line helper for cases the existing sequential client cannot express; it verifies the same owned `getPaths` roots before sending malformed/pipelined work. Ordinary tests continue to use the existing strict client.

Render tests check path/duration/readiness errors, idle status/cancel, a two-frame H.264 export, active-export rejection, cancellation, and late encoder failure from an owned directory used as a file target. Completion is established through artifact checks and a separate decode through the existing VideoClip operator, then image decoding/pixel comparison. The initial 64-by-32 success-case attempt was rejected by this machine's h264_nvenc minimum dimensions; the success fixture now uses 256-by-256. That initial failure remains in `tixl-contract-run-739f58d998a242818dd84a4f8ba1668d` and demonstrates why acceptance is not completion. Successful render artifacts and decoded frames are retained with the passing run evidence.

DB-09 remains open for mixed-format concurrent output captures, readback/resource-disposal failures, minimized/obscured window behavior, encoder/device variants, audio-enabled export, nonfinite/extreme render arguments and Linux/macOS runs (including the expected unsupported Windows-capture branch). No file-saved guarantee or platform/codec parity is inferred from the tested configurations.

## Playback/audio and wire contract checkpoint

`Catalog/playback-audio.json` adds the remaining seven command names. All authored groups now match the dispatcher exactly for every declared target, checked by `CombinedCatalog_MatchesEveryTargetDispatchExactly`; adding or removing a callable command fails this gate until the catalog matches. The existing metadata/example checks remain. This is command-name completeness, not a claim that every behavior or platform has passed.

Three playback cases verify bars-over-seconds and speed-over-playing precedence, BPM conversion, negative time/speed, omission versus explicit null, the `isPlaying` threshold, low frame-count clamping, and out-of-order deferred replies. Four wire cases cover blank/malformed lines, absent/null/unknown/scalar methods, arbitrary JSON IDs and duplicate IDs, competing connections with the same ID, and a mutation that still executes after its sender disconnects behind a bounded stall. Clients must correlate replies, use their own unique IDs, and reconcile state after disconnect instead of replaying unknown mutations.

Two audio cases inspect device shapes/levels, idle/repeated stop, and an unknown device selection that is acknowledged but never becomes active. Empty/missing/null device returns to project audio. No real device is selected or recorded. `Recording_WithoutDevicesReturnsNullAndNoFile` is implemented and compiles, but is **not run** on this workstation: it asserts an empty device inventory before requesting recording and belongs to the separate `AudioDeviceAbsence` group. Source inspection shows recording may fall back to the first enumerated input even when a configured name is invalid; an invalid name is therefore not a safe way to simulate missing hardware.

DB-10 remains open for a provisioned no-device run, isolated synthetic-input recording with finalized WAV/sample checks, start-twice and device-switch failures, malformed object/array method crash characterization, nonfinite/extreme arguments, upper pump bound, server/process restart and pending shutdown behavior, and Linux/macOS execution. The ordinary `Audio` group is safe for non-capturing checks; do not include recording cases in workstation runs without their isolation prerequisites. The shared Windows assembly setting was restored and all 18 owned roots/process cleanups were verified after the nine-case-per-backend run. The catalog's recording verification reference identifies pending evidence, not a passed recording contract.

## Offline documentation checkpoint

`Tools/DebugBridgeCatalog` now supports `generate` and `check` from the repository root, or with `--repo <root>`. It validates every authored group, merges commands in ordinal order, checks exact per-target dispatch names, and resolves verification IDs to xUnit source methods under `Tests` while ignoring build output. It writes `.agentic/DEBUG_BRIDGE_REFERENCE.md` and `.agentic/debug-bridge/catalog.json` without loading Editor/Core or scanning operator packages. The protocol guide links these files and describes the maintenance/client workflow. Generation does not execute live tests or infer their status.

Two consecutive real generations on 2026-11-08 with no TiXL process had SHA-256 hashes `64BEBB0E857A80C13C17CB0B77DE4E9A422BC9FE8687F671509DBDBB71BDA417` (Markdown) and `21E8BEFE0130994783440E1E1B644F6013C75CBB722AB7A39CE4C11CABAC7724` (JSON). The same inputs pass `check`; tracked files contain no build timestamp or current-HEAD hash. Synthetic tests reject missing/changed/obsolete output and require metadata plus a resolvable test reference for a newly added command. A previously undeclared conditional symbol cannot hide a command across all targets: it fails for target-definition review. The current source has no conditional dispatch cases; configured `TRACE;RELEASE` matches the evaluated Release project property, without implying runtime OS/backend parity.

Documentation generation proceeded after complete command metadata and source verification references existed, while remaining Sprint 2 runtime prerequisites stayed open. The generated reference prominently links this evidence ledger. This is an independently validated documentation checkpoint, not a declaration of complete behavior coverage, enforced CI, platform support or release readiness. Handler-only mutation proof, CI enforcement and release provenance/payload/rollback checks remain pending.

## Offline CI configuration checkpoint

The pull-request workflow now has a Windows/Ubuntu/macOS offline matrix and a stable aggregate check named `Debug Bridge docs and offline tests`. It runs catalog tests (including missing-command, stale-output, schema and unresolved-reference failures), exact generated-output checks, and only `Category=Offline` client/isolation tests. It does not launch TiXL or build Editor/Core. The matrix runs for PRs, manual invocation, and pushes to the implementation branch; the existing full build remains limited to non-draft PRs. No path filter can silently omit this proposed required check.

The same Release commands pass locally: 28 catalog tests, the full generated check and 23 offline integration tests. Remote matrix execution, repository required-check settings, provisioned live workflow/runners and live evidence remain pending; workflow YAML alone is not evidence of enforcement or a passing remote run.
