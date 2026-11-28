# DrawGeometryIndirectExample

Pin **DrawGeometryIndirectExample → Image** and play the timeline for an eight-bar orbit. The Mode dropdown selects the draw path. Enable **Benchmark** for a deterministic replay. For an adjacent-frame comparison of LOD with and without depth culling, enable **PairDepthModes** before starting the benchmark. The Log window prints the CSV path. Disable Benchmark between runs.

## LOD, culling and combined examples

[Three matched reference comparisons](three-comparisons.md) · [GPU graph](three-comparisons.png) · [Side-by-side MP4](indexed-examples.mp4)

[Three runnable examples, matched GPU measurements, external baseline and macOS fallback](examples.md)

In the earlier Auto video and tables below, **original indirect** means this branch at `d3ba2162e`. The external H445/tixl-culling-lod comparison is in the linked example report.

## Adaptive depth on the moving camera

Enable **DepthCulling** and **AutoDepth** on DrawGeometryIndirect, or select **IndirectLodAuto** in the example. Auto times an off/on pair in alternating order, polls for completed GPU timestamps without flushing, and keeps the incumbent unless the other path wins by roughly 2.5%. It waits eight evaluations before probing again. Turning DepthCulling off forces the LOD-only path.

[Watch the 20-second comparison](auto-depth.mp4) · [All 28,800 GPU timings and per-pose choices](auto-sweep.csv)

| Hidden post-LOD triangles | Samples per path | Standard (ms) | Original indirect (ms) | Always off (ms) | Always on (ms) | Auto (ms) | Auto used depth | Auto − replay (µs) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 0% | 2095 | 0.151 | 0.113 | 0.066 | 0.072 | 0.066 | 13% | +0.1 |
| >0–10% | 1285 | 0.150 | 0.118 | 0.067 | 0.074 | 0.068 | 10% | +0.2 |
| >10–20% | 655 | 0.152 | 0.128 | 0.072 | 0.078 | 0.072 | 8% | +0.0 |
| >20–30% | 110 | 0.149 | 0.133 | 0.072 | 0.077 | 0.074 | 14% | +0.3 |
| >30–50% | 265 | 0.145 | 0.140 | 0.070 | 0.074 | 0.070 | 10% | +0.2 |
| >50–75% | 170 | 0.146 | 0.139 | 0.078 | 0.080 | 0.079 | 18% | +0.4 |
| >75–100% | 220 | 0.126 | 0.164 | 0.070 | 0.063 | 0.063 | 81% | +0.0 |
| All | 4800 | 0.150 | 0.118 | 0.068 | 0.074 | 0.068 | 15% | +0.1 |

The median paired difference between Auto and a forced replay of the exact same decisions was **+0.1 µs** over the sweep. Individual pass medians ranged from +0.0 to +0.4 µs. This is an end-to-end GPU estimate with run-to-run variation. It includes timestamp commands and their effects on rendering. CPU submission time is outside the GPU interval. Exploration and delayed switching are included in Auto’s reported frame times.

Every wall uses five passes and the same 120 camera angles and 24 warm-up poses as the previous sweep. Each policy follows a continuous orbit, rotating the order of the five blocks between passes. The replay block immediately follows Auto and forces its recorded choice for each angle. Standard is TiXL DrawMeshUnlit at full detail. Original indirect is the LOD-plus-depth reducer, classifier and dispatch dimensions from d3ba2162e. All four indirect comparisons use matching LOD settings. All columns report median full-scene GPU times at 1280 × 720 on the GTX 1660 Ti, excluding presentation and editor UI. The hidden fraction uses the earlier GPU command counts at the identical wall and pose.

| Wall | Standard (ms) | Original indirect (ms) | Always off (ms) | Always on (ms) | Auto (ms) | Auto used depth |
|---|---:|---:|---:|---:|---:|---:|
| original | 0.147 | 0.119 | 0.066 | 0.072 | 0.066 | 9% |
| none | 0.146 | 0.109 | 0.062 | 0.068 | 0.062 | 9% |
| narrow | 0.152 | 0.116 | 0.067 | 0.073 | 0.067 | 9% |
| medium | 0.151 | 0.114 | 0.067 | 0.072 | 0.067 | 30% |
| wide | 0.154 | 0.119 | 0.071 | 0.076 | 0.070 | 32% |
| innerSmall | 0.149 | 0.117 | 0.068 | 0.074 | 0.068 | 9% |
| inner | 0.151 | 0.131 | 0.074 | 0.080 | 0.074 | 9% |
| innerOffset | 0.152 | 0.127 | 0.070 | 0.078 | 0.071 | 9% |

### Switching cost and crossover

The CPU controller took **0.3 µs median, 0.74 µs mean and 1.9 µs at P95**. A separate replay timed path selection, query polling and both probe timestamp hooks using Stopwatch on three orbits per wall. The measurement includes timer-call overhead and excludes the mesh draw and result logging. CPU and GPU durations can overlap. [2,880 CPU samples](auto-cpu.csv)

Across all poses, Auto used 55% less median GPU time than standard draw and 43% less than the original indirect implementation. Above 75% hidden, always-on beat always-off by a median 6.0–7.4 µs in each of the five passes. Auto used depth on 81% of those frames. At 50–75% hidden, keeping depth off still won in each pass. The observed crossover is a band for this scene and clock regime. Sparse coverage, the cost of the surviving triangles, and camera motion all affect it.

VSync was disabled in the study profile. Every one of the 49 clock checks for the included orbits reported P2, 5,750 MHz memory and 1,560–1,950 MHz graphics. A first run crossed power states and was discarded. Earlier measurements below used different clock conditions and show a different crossover. For image verification, the Auto replay was captured at 120 poses. RGB output matched the LOD-only path pixel-for-pixel at 0°, 90°, 180° and 270°. [Clock samples](auto-clocks.csv)

To reproduce the six-path comparison, apply [the GPU diagnostic patch](auto-profile.patch) to a disposable checkout of commit `2f9243270`, build Lib and Examples, and run the example benchmark once for each wall in the table below with PairDepthModes disabled. Mode IDs in the diagnostic CSV are 0 standard, 1 forced decision replay, 2 always off, 3 original indirect, 4 always on and 5 Auto. The depth_used field records the sampled decision on mode 5. For CPU timings, start with another clean checkout of `2f9243270` and apply [the CPU diagnostic patch](auto-cpu-profile.patch). It runs three Auto-only orbits per wall.

## Earlier paired break-even on a GTX 1660 Ti

Across eight wall configurations and 120 camera angles, the first 10-percentage-point band with positive median savings on every pass was **over 10% through 20% of post-LOD triangles rejected**. Median savings were 0.035 ms in that band and 0.287 ms above 75% rejected. The original wall rejected just 4.6% at the median camera position, which explains why LOD alone remained slightly faster for its complete orbit.

![Paired GPU time savings versus geometry rejected](depth-crossover.png)

| Triangles rejected | Wall/pose combinations | LOD only (ms) | LOD + depth (ms) | Paired savings (ms) | Five pass medians (ms) |
|---|---:|---:|---:|---:|---:|
| 0% | 419 | 1.083 | 1.118 | -0.032 | -0.040 to -0.028 |
| >0–10% | 257 | 1.071 | 1.070 | -0.009 | -0.010 to -0.003 |
| >10–20% | 131 | 1.097 | 1.026 | +0.035 | +0.015 to +0.058 |
| >20–30% | 22 | 1.094 | 0.978 | +0.103 | +0.090 to +0.119 |
| >30–50% | 53 | 1.002 | 0.821 | +0.155 | +0.132 to +0.177 |
| >50–75% | 34 | 0.921 | 0.784 | +0.131 | +0.059 to +0.142 |
| >75–100% | 44 | 0.756 | 0.473 | +0.287 | +0.279 to +0.291 |

Each pair repeats one camera pose on adjacent frames. The modes exchange order across poses and passes. Savings are the median of the within-pair time differences. The two timing columns are separate medians and can differ from their paired difference. The five-pass range describes repeatability on this machine. Hidden objects differ in size and screen area, so rejection percentage alone does not determine the savings. [All 4,800 frame pairs and GPU counts](depth-sweep.csv)

## Where the time went

The original reducer read the full current depth target in 14,400 workgroups at 1280 × 720. Each group produced one 8 × 8 tile maximum and used seven shared-memory barriers. The optimized reducer produces eight neighboring tiles per workgroup, with coalesced horizontal loads, one shared-memory barrier and 1,800 workgroups. A clear depth sample finishes its column early. MSAA reduction includes every sample that could increase the maximum.

The original classifier used a single invocation per part to scan its projected rectangle. Fully covered parts required the whole scan. The depth path now uses one 64-thread group per part. A center-tile test retains obviously visible parts immediately. For the rest, the lanes share the rectangle scan and keep the part if any tile fails the occlusion test. The depth-disabled path still classifies 64 parts per group. Both versions use the same conservative tile footprint, near-plane handling and depth bias.

| Wall | Version | Depth tile build (µs) | Classification (µs) | Indexed draw (µs) |
|---|---|---:|---:|---:|
| original | legacy | 182.3 | 65.8 | 817.0 |
| original | optimized | 42.0 | 43.0 | 829.4 |
| wide | legacy | 184.8 | 44.1 | 891.0 |
| wide | optimized | 50.6 | 40.8 | 886.8 |

With the original wall, tile-build time fell by **77%**. In the wide-wall views where all triangles were rejected, classification fell from **310.3 to 53.5 µs**. The legacy and optimized classifiers submitted identical part and triangle counts at every checked pose for both walls. Stage numbers are medians from a diagnostic replay with GPU timestamp boundaries and indirect-command readback. [Stage measurements](depth-stage-profile.csv)

For the end-to-end A/B check, both variants used LOD and depth culling. Legacy used the shaders and dispatch dimensions from commit `d3ba2162e`. The variants alternated at each pose within the same editor process. The timed replay used only the outer scene timestamps. The readbacks and internal timestamps ran separately.

| Wall | Legacy scene (ms) | Optimized scene (ms) | Paired savings (ms) |
|---|---:|---:|---:|
| original | 1.254 | 1.079 | +0.174 |
| wide | 1.376 | 1.220 | +0.195 |

[All 1,200 before/after frame pairs](depth-ab.csv)

## Original moving-camera benchmark

| Draw path | Median (ms) | P95 (ms) | 0–90° | 90–180° | 180–270° | 270–360° |
|---|---:|---:|---:|---:|---:|---:|
| Standard draw | 1.164 | 3.654 | 1.151 | 1.207 | 1.145 | 1.147 |
| Indirect only | 1.795 | 1.977 | 1.615 | 1.911 | 1.813 | 1.599 |
| Indirect + LOD | 1.008 | 1.154 | 0.995 | 1.043 | 1.005 | 0.968 |
| Indirect + depth | 1.599 | 1.829 | 1.006 | 1.677 | 1.665 | 1.344 |
| Indirect + LOD + depth | 1.030 | 1.169 | 0.992 | 1.081 | 1.025 | 1.033 |

![GPU timings during the orbit, with four matching camera views](camera-orbit.png)

The quarter-orbit columns are medians in milliseconds. Each plotted point is a 15° median. The five-mode run measures one complete orbit per mode and rotates their order across passes. GPU clocks were driver-managed. Use the adjacent-frame A/B figures above when comparing versions. [All 3,000 moving-camera timestamps](gpu-times.csv)

## Workload and reproduction

- Windows, TiXL Debug, Vulkan, NVIDIA GeForce GTX 1660 Ti, driver 610.88. The example renders a 1280 × 720 RGBA16F target with single-sample D32 depth. The GPU timestamps bracket the complete scene render, including the clear, wall, depth processing and drawing. Editor UI and presentation are outside the interval.
- The scene uses 225 toruses on a 15 × 15 grid, with 576 fine triangles or 64 coarse triangles per part. LOD switches below 100 projected pixels. The standard path draws a single combined full-detail mesh through GeometryToMesh and DrawMeshUnlit. Both paths draw the opaque wall first and keep hardware depth testing enabled.
- For the sweep, set the example's **WallSize** and **WallCenter** as below. Run the paired benchmark for each setting. Every pass warms up for 24 frames, then advances by 3° after each frame pair. Five passes give 600 pairs per wall. The original five-mode run warms up for 24 frames before each 120-frame orbit.
- For rejection counts, a separate profiling replay read the indexed indirect records after classification. Submitted triangles are the sum of indexCount × instanceCount / 3. The hidden fraction is one minus the depth-on count divided by the depth-off count at the same LOD and pose. The fraction excludes triangles already removed by frustum culling and LOD.
- Captured LOD images with depth culling on and off matched pixel-for-pixel at 0°, 90°, 180° and 270° on the default wall. GPU readback tests verify farthest-depth reduction on partial tiles, including a single uncovered sample in a 4× MSAA pixel.

| CSV wall ID | WallSize (X, Y, Z) | WallCenter (X, Y, Z) |
|---|---|---|
| none | 0.001, 0.001, 0.01 | 0, 1, 16 |
| original | 19, 4.5, 0.4 | 0, 1, 5 |
| narrow | 6, 10, 0.4 | 0, 2, 16 |
| medium | 18, 20, 0.4 | 0, 3, 16 |
| wide | 40, 30, 0.4 | 0, 3, 16 |
| innerSmall | 14, 12, 0.4 | 0, 2, 0 |
| inner | 36, 15, 0.4 | 0, 2, 0 |
| innerOffset | 28, 14, 0.4 | 0, 2, 8 |
