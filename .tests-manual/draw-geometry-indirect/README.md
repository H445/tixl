# DrawGeometryIndirectExample

Open the Examples package and pin **DrawGeometryIndirectExample → Image**. Play the timeline for an eight-bar orbit. The **Mode** dropdown selects the standard path or any of the four indexed indirect configurations. The camera moves from a low view behind the wall at 0° to the opposite side at 180°, then returns.

## GPU results

TiXL Debug build on Windows, Vulkan, GeForce GTX 1660 Ti, NVIDIA driver 610.88. The example renders 1280 × 720 to an RGBA16F target with single-sample D32 depth. Values are milliseconds. P95 is the nearest-rank 95th percentile. Each angle column is the median for that quarter of the orbit.

| Draw path | LOD | Depth culling | Median | P95 | 0–90° | 90–180° | 180–270° | 270–360° |
|---|---|---|---:|---:|---:|---:|---:|---:|
| DrawMeshUnlit | Off | Off | 1.238 | 3.755 | 2.944 | 3.535 | 1.199 | 1.131 |
| Indexed indirect | Off | Off | 1.771 | 1.951 | 1.399 | 1.842 | 1.813 | 1.689 |
| Indexed indirect | On | Off | 1.034 | 1.190 | 0.999 | 1.024 | 1.051 | 1.064 |
| Indexed indirect | Off | On | 1.779 | 1.959 | 1.443 | 1.846 | 1.783 | 1.373 |
| Indexed indirect | On | On | 1.176 | 1.332 | 1.142 | 1.179 | 1.186 | 1.186 |

LOD alone had the lowest overall median, 1.034 ms. Adding depth culling raised that to 1.176 ms. The standard path varied substantially across the five passes, with per-pass medians from 1.161 to 3.518 ms. The results describe this scene and driver-managed clock regime.

![GPU timings during the orbit, with four matching camera views](camera-orbit.png)

Each plotted point is the median of 25 GPU frames within a 15° interval. The four stills show the combined LOD and depth mode. [Download all 3,000 timestamp samples](gpu-times.csv).

## Workload and timing

- The scene contains 225 toruses on a 15 × 15 grid. Each part has 576 full-detail triangles and 64 low-detail triangles. Both paths draw the same opaque wall first and perform normal hardware depth testing and writing.
- The baseline is a single combined full-detail mesh through **GeometryToMesh → DrawMeshUnlit**, with vertex color enabled. The indirect branch uses the same source geometry and camera, with frustum culling always enabled. LOD switches to the coarse mesh below 100 projected pixels. Depth culling builds the depth tiles from the current wall pass. The standard and indirect nodes run their own unlit shaders, so the baseline comparison includes shader and draw-path differences.
- The optional **Benchmark** switch runs 24 warm-up frames before each 120-frame orbit. The camera advances exactly 3° per frame. Five passes rotate the mode order so every mode occupies every run position once. The timeline can stay paused. Leave the example pinned until the Log window prints the CSV path, then disable Benchmark to reset it.
- A pair of GPU timestamps brackets evaluation of the complete scene render target, including its clear, wall, culling, LOD selection and drawing. Query results are read from an eight-slot ring. The reported duration excludes editor UI and presentation. Shader and static geometry preparation occur during warm-up. GPU clocks were managed by the driver. For an indication of run variation, split the raw samples by the pass column.
- At 0°, 90°, 180° and 270°, toggling depth culling at full detail produced identical captured images. The mean absolute difference between the standard and full-detail indirect renders was below 0.001 on the 0–255 RGB scale at each view.
