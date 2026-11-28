# Indexed LOD and culling examples

Pin the Image output and play the timeline. All three named examples use the same 225-ring field, opaque wall, two mesh resolutions, camera and render target. Each wraps the corresponding mode of DrawGeometryIndirectExample.

| Example | Frustum rejection | Depth rejection | LOD |
|---|---|---|---|
| IndexedLodExample | Off | Off | On |
| IndexedCullingExample | On | On | Off |
| IndexedCullingLodExample | On | On | On |

The full-detail control disables both part rejection and LOD. All paths retain hardware clipping and depth testing. In the benchmark dropdown the controls are IndirectUnculled and IndirectLodOnly. The older IndirectLod mode includes frustum rejection. IndirectDepth and IndirectLodAndDepth force current-frame depth culling on; Auto is measured separately.

[All three comparisons against H445/tixl-culling-lod](three-comparisons.md) · [Updated MP4](indexed-examples.mp4) · [Graph](three-comparisons.png)

## Earlier culling-only comparison with H445/tixl-culling-lod

The comparison uses the reference repository at f5d5558db328cf3c7f8a1b8daa82f4022c2bffcc and its culling component at 3b9a27f8f6f8b278b1220ff6555a012794a98e79. We ran its DepthCullPass and static triangle classifier against the same wall and full-detail field. Both paths use the same unlit appearance and the same camera. The reference compacts triangle groups into a draw. The new path rejects mesh parts and issues indexed multi-draw. The diagnostic log confirmed selection of the reference MarkDepth shader on the active depth attachment.

At each pose, five consecutive frames render the five policies. The starting policy rotates across poses and passes. There are five passes over 120 poses per wall. Table entries are median / P95 GPU milliseconds. The reference column measures full-detail culling only. The reference project’s combined conservatory uses different geometry and LOD tiers.

| Wall | Unculled full detail | LOD only | Indexed culling only | Indexed culling + LOD | Reference culling only |
|---|---:|---:|---:|---:|---:|
| original | 0.092 / 0.098 | 0.068 / 0.076 | 0.088 / 0.101 | 0.070 / 0.082 | 0.186 / 0.217 |
| none | 0.091 / 0.096 | 0.066 / 0.071 | 0.094 / 0.099 | 0.070 / 0.075 | 0.202 / 0.211 |
| narrow | 0.093 / 0.097 | 0.068 / 0.072 | 0.095 / 0.100 | 0.071 / 0.076 | 0.201 / 0.207 |
| medium | 0.096 / 0.102 | 0.072 / 0.077 | 0.098 / 0.105 | 0.074 / 0.082 | 0.205 / 0.212 |
| wide | 0.098 / 0.108 | 0.074 / 0.081 | 0.100 / 0.112 | 0.078 / 0.086 | 0.207 / 0.217 |
| innerSmall | 0.096 / 0.102 | 0.072 / 0.080 | 0.092 / 0.097 | 0.076 / 0.084 | 0.187 / 0.204 |
| inner | 0.104 / 0.111 | 0.080 / 0.090 | 0.093 / 0.099 | 0.083 / 0.094 | 0.172 / 0.186 |
| innerOffset | 0.102 / 0.106 | 0.076 / 0.087 | 0.096 / 0.104 | 0.080 / 0.088 | 0.197 / 0.209 |

The following medians pool matched frames from all eight walls, in milliseconds. The hidden fraction uses the prior study’s post-LOD triangle counts at the identical wall and pose. Each camera angle contributes five samples per wall.

| Hidden after LOD | Full detail control | LOD only | Indexed culling | Indexed culling + LOD | Reference culling |
|---|---:|---:|---:|---:|---:|
| All poses | 0.095 | 0.072 | 0.094 | 0.074 | 0.197 |
| 0% | 0.094 | 0.070 | 0.096 | 0.072 | 0.204 |
| >0–25% | 0.097 | 0.073 | 0.093 | 0.076 | 0.187 |
| >25–75% | 0.099 | 0.078 | 0.086 | 0.079 | 0.160 |
| >75–100% | 0.090 | 0.072 | 0.064 | 0.064 | 0.098 |

On the default wall, comparing RGB output for reference and indexed full-detail culling found 0, 2, 8 and 5 differing pixels out of 921,600 at 0°, 90°, 180° and 270° respectively. The reference compacts groups of triangles and the new renderer uses part bounds.

[Matched per-pose timings](reference-culling-times.csv) · [Reference project at the tested revision](https://github.com/H445/tixl-culling-lod/tree/f5d5558db328cf3c7f8a1b8daa82f4022c2bffcc)

For independent context, the reference project reports 0.434 ms for LOD only, 0.575 ms for culling only and 0.477 ms for both in its 1× conservatory. At 100× it reports 4.358, 16.904 and 3.584 ms respectively. These are the reference author’s measurements on the conservatory. Only the full-detail culling path was ported into the matched ring-field replay. [Original benchmark and workload](https://github.com/H445/tixl-culling-lod/blob/f5d5558db328cf3c7f8a1b8daa82f4022c2bffcc/TiXL/CombinedConservatory/README.md)

To reproduce the four-way control in TiXL, enable Benchmark on any of the three examples. The Log shows the CSV. To reproduce the five-way comparison, check out the reference revision with submodules, then run reference-culling.py with its path from a disposable feature-branch checkout. Build Lib and Examples, select CompareExamples and Benchmark in DrawGeometryIndirectExample, and repeat for the eight wall configurations. Diagnostic mode IndirectLod holds the external full-detail reference.

## Vulkan submission and macOS

DrawGeometryIndirect produces one 20-byte indexed indirect record per mesh part. Culled records have zero instances. The study device reported multiDrawIndirect and drawIndirectFirstInstance enabled, with maxDrawIndirectCount of 4,294,967,295. Its 225 draws fit in a single call. The Vulkan backend issues vkCmdDrawIndexedIndirect in batches up to maxDrawIndirectCount when multiDrawIndirect is available, and loops over single-record indexed indirect calls when it is unavailable. This feature fallback issues GPU-generated or CPU-generated records. [Khronos multi-draw sample](https://docs.vulkan.org/samples/latest/samples/performance/multi_draw_indirect/README.html)

On macOS the operator selects CPU command generation automatically. The CPU projects each part’s bounds, tests the six frustum planes, selects the LOD index range and uploads the records. Current-frame depth occlusion still runs on the GPU and can suppress the CPU-selected draw. Set TIXL_CPU_INDEXED_INDIRECT=1 before launching TiXL to exercise the same producer on another OS. Native macOS/MoltenVK execution has not been tested here.

The forced CPU and GPU command producers returned pixel-identical RGB images for all three named examples at 0°, 90°, 180° and 270° on Windows Vulkan (12 comparisons). Each example’s Benchmark switch runs 2,400 timed scene evaluations: four paths at 120 poses over five passes.

## Supplemental eight-mode orbit

Median scene GPU milliseconds on a GTX 1660 Ti, 1280 × 720 Vulkan, with VSync disabled and driver-managed clocks. Each wall has 120 poses spaced by 3°, five passes, and 24 warm-up frames per mode and pass. Each timing brackets the wall, classification and draw. These longer, consecutive-mode blocks experienced substantial GPU power-state variation. Use the adjacent-frame comparison above for relative performance. Workload and GPU power state affect absolute times.

| Wall | Unculled, full detail | LOD only | Culling only | Culling + LOD |
|---|---:|---:|---:|---:|
| original | 1.753 | 1.163 | 1.300 | 1.037 |
| none | 0.670 | 1.183 | 1.825 | 1.061 |
| narrow | 1.986 | 1.206 | 1.857 | 1.098 |
| medium | 0.686 | 1.192 | 1.881 | 1.069 |
| wide | 1.897 | 1.187 | 1.849 | 1.129 |
| innerSmall | 0.650 | 0.430 | 0.642 | 0.440 |
| inner | 0.103 | 0.079 | 0.090 | 0.080 |
| innerOffset | 0.102 | 0.076 | 0.096 | 0.080 |

The default wall had these median / P95 scene GPU times in milliseconds.

| Path | Median | P95 |
|---|---:|---:|
| Full detail, no part culling | 1.753 | 2.088 |
| LOD only | 1.163 | 1.323 |
| Frustum + depth | 1.300 | 1.853 |
| Frustum + depth + LOD | 1.037 | 1.199 |

[All 38,400 timestamps, including frustum-only, Auto and standard draw](example-gpu-times.csv)
