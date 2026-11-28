---
id: cull-mesh
title: Mesh frustum and depth culling
scope: rendering
tags: [rendering, mesh, vulkan]
added: 2026-11-27
added-in-version: 4.4
prerequisites:
  - Open [CullMeshExample] and view the Preview render target.
  - Run the Vulkan steps with the default editor backend. For the fallback step, use Windows and start a second editor with TIXL_BACKEND=d3d11.
  - Use a GPU frame capture with indirect draw arguments visible for the submission checks.
---

## Step: Compare the camera toggle and draw order

**Action:** Pin Preview and open Performance. Pause playback. Set Amplitude and Offset on the wall's AnimValue to zero and set Wall mesh Center to (0, 0, 2). Leave the Vector3 and all sphere transforms as authored. Disable Mesh Occlusion Culling on Camera and capture the preview. Enable the toggle and allow several frames for GPU readback.

**Expected:** Triangles reads 4,236 throughout (12 wall triangles and 1,056 per sphere). With culling disabled, five direct draws render the scene and the material-pass counter is hidden. With culling enabled, the preview matches the reference image. The order of the inputs on Draw order controls how much depth is available when each mesh draws.

| Vulkan, centered wall | Indirect material submissions in execution order | Triangles drawn (material pass) |
| --- | --- | ---: |
| Draw wall first (example default) | 8, 0, 512, 0, 0 | 520 |
| Move only Draw wall to the last input | 1,056, 1,056, 1,056, 1,056, 8 | 4,232 |

**GPU verification:** Capture each order with RenderDoc. For each of the five material draws, divide the indirect vertex count by three and multiply by the instance count. Sum the five results and compare to Performance. The five private depth draws submit another 4,236 triangles in each order. Captures for both orders should have identical output pixels.

## Step: Camera, topology and animation

**Action:** In each order move Wall mesh Center to (100, 0, 2). Pan the camera and target together to X = -100. Restore the camera and change one sphere from 32 by 16 segments to 8 by 4. Resume the wall animation with its original values. Repeat the silhouette comparison at 1 and 4 samples on hardware supporting 4x MSAA.

**Expected:** With the wall outside the camera, the material count reads 4,224. With every mesh outside, it reads zero. Reducing one sphere changes Triangles to 3,252. GPU readback can trail the animation. After pausing, compare the settled count to a capture at that pose.

## Step: Direct3D 11 fallback

**Action:** Repeat the draw-order and visibility tests with Direct3D 11 and the original 32 by 16 sphere segments.

**Expected:** Five direct draws render in either order. With the toggle enabled both triangle counts read 4,236, including with every mesh outside the view. Disabling the toggle hides the material-pass count. The console reports no shader errors.
