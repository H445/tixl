# Three matched rendering comparisons

[Performance graph](three-comparisons.png) · [18-second MP4](indexed-examples.mp4) · [14,400 paired samples](six-mode-times.csv)

| Mode | Reference GPU (ms) | Indexed GPU (ms) | GPU time saved | Reference CPU (ms) | Indexed CPU (ms) |
|---|---:|---:|---:|---:|---:|
| LOD only | 0.099 | 0.090 | 8.8% | 0.173 | 0.077 |
| Culling only | 0.234 | 0.111 | 52.7% | 0.132 | 0.084 |
| Culling + LOD | 0.159 | 0.092 | 42.1% | 0.968 | 0.082 |

Medians cover all eight walls, 120 camera poses per wall and five passes. Savings compare the two GPU medians. CPU measures scene evaluation and command submission on the calling thread. GPU timestamps bracket the full scene including the wall. The two durations can overlap.

| Wall | LOD only, ref / indexed | Culling only, ref / indexed | Both, ref / indexed |
|---|---:|---:|---:|
| original | 0.098 / 0.089 | 0.233 / 0.106 | 0.160 / 0.091 |
| none | 0.090 / 0.082 | 0.238 / 0.106 | 0.153 / 0.084 |
| narrow | 0.095 / 0.086 | 0.241 / 0.111 | 0.156 / 0.088 |
| medium | 0.100 / 0.092 | 0.248 / 0.115 | 0.160 / 0.092 |
| wide | 0.103 / 0.096 | 0.246 / 0.117 | 0.160 / 0.095 |
| innerSmall | 0.098 / 0.091 | 0.227 / 0.109 | 0.159 / 0.093 |
| inner | 0.109 / 0.100 | 0.211 / 0.113 | 0.165 / 0.104 |
| innerOffset | 0.104 / 0.096 | 0.229 / 0.115 | 0.165 / 0.098 |

Wall entries are GPU milliseconds.

## Matched workload

The pinned reference and feature branch ran in the same TiXL Vulkan process on a GeForce GTX 1660 Ti at 1280 × 720. At each angle six consecutive frames exercised all six paths. The starting path rotated across steps and passes. Each pass warmed up for 24 frames. Both renderers used the same 225 ring placements, wall, camera, render target, fine and coarse torus topology (576 and 64 triangles), two-tier 100-pixel threshold and unlit vertex colors. VSync was disabled. The 56 clock polls observed P2 with graphics clocks of 1200–1485 MHz.

The reference LOD-only graph uses SplitPointsByLod, PointListToGpuBuffer and the stock DrawMeshAtPoints draw for each tier. Its combined graph enables frustum rejection in the tier splitter and runs DepthCullPass and DrawCulledCopies on the occupied tiers. Live depth was confirmed for both 576-face and 64-face copies. Full-detail culling uses the reference triangle classifier from the previous comparison. The indexed paths use DrawGeometryIndirect. Depth is forced on for the two culling modes.

The reference uses a 0.87-unit bounding sphere to select tiers. The indexed path uses projected part bounds. At the same threshold they can select different detail near a transition. The reference batches copies of a shared ring by tier. The indexed renderer draws parts from packed geometry. These results measure the complete rendering paths. [Pinned reference](https://github.com/H445/tixl-culling-lod/tree/f5d5558db328cf3c7f8a1b8daa82f4022c2bffcc)

Across the five individual passes the GPU savings ranges were LOD only: 6.7–9.5%, Culling only: 52.6–52.9%, Culling + LOD: 40.7–42.4%.

At 30 evenly spaced default-wall views, the median share of pixels differing by more than 24/255 in any RGB channel was 0.239% for each LOD comparison (maximum 0.850%). The culling-only comparison peaked at 0.00152%. Within each renderer, enabling depth and frustum culling alongside LOD changed zero pixels above that threshold across all 30 views. The MP4 uses those paired captures.

## Reproduction

From a disposable feature-branch checkout run reference-lod.py with the pinned reference checkout (including submodules) as its argument. Build Lib and Examples. In DrawGeometryIndirectExample enable CompareExamples and Benchmark. Replay each of the eight wall configurations in the [orbit study](README.md). The diagnostic reuses mode IDs. Indirect and IndirectLodOnly are the reference and indexed LOD pair, IndirectLod and IndirectDepth the culling pair, and IndirectLodAuto and IndirectLodAndDepth the combined pair. The normalized CSV labels each measurement explicitly.
