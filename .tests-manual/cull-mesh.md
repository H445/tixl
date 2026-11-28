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

**Action:** Pin Preview and open Performance. Pause playback and turn off Idle Motion so the wall holds the same pose between captures. Keep the authored wall animation and all sphere transforms. With the example's default tempo, seek to each time below. Compare Mesh Occlusion Culling off and on, allowing several frames for GPU readback. Repeat after moving only Draw wall to the last input of Draw order.

**Expected:** Triangles stays at 4,236 (12 wall triangles and 1,056 per sphere). With culling off, five direct draws render the scene and the material-pass counter is hidden. At each pose the output pixels match across draw order and camera toggle settings.

| Animation time (seconds) | Vulkan material triangles, wall first | Vulkan material triangles, wall last |
| ---: | ---: | ---: |
| 0.0 | 1,672 | 1,672 |
| 0.5 | 1,670 | 1,670 |
| 1.0 | 1,830 | 1,830 |
| 1.5 | 2,086 | 2,086 |

**GPU verification:** Capture each order with RenderDoc at 1.5 seconds. All five private depth draws (4,236 triangles) precede the depth reduction and all five indirect material draws. For each indirect draw, divide the vertex count by three and multiply by the instance count. The sum should equal the material-pass counter in both orders.

## Step: Camera, topology and animation

**Action:** In each order move Wall mesh Center to (100, 0, 2). Pan the camera and target together to X = -100. Restore the camera and change one sphere from 32 by 16 segments to 8 by 4. Resume playback. Repeat the silhouette comparison at 1 and 4 samples on hardware supporting 4x MSAA.

**Expected:** With the wall outside the camera, the material count reads 4,224. With every mesh outside, it reads zero. Reducing one sphere changes Triangles to 3,252. GPU readback can trail the animation. After pausing, compare the settled count to a capture at that pose.

## Step: Transparent wall

**Action:** At 0.5 seconds, set Draw wall to Normal blending, disable its depth writes and set its color alpha to 0.35. Compare the preview with culling off and on.

**Expected:** The four opaque spheres contribute four depth draws, followed by five material draws. The wall remains translucent and the pixels match the unculled reference.

## Step: Direct3D 11 fallback

**Action:** Repeat the draw-order and visibility tests with Direct3D 11 and the original 32 by 16 sphere segments.

**Expected:** Five direct draws render in either order. With the toggle enabled both triangle counts read 4,236, including with every mesh outside the view. Disabling the toggle hides the material-pass count. The console reports no shader errors.

## Step: Camera culling with DrawMeshAtPoints

**Action:** Replace the four sphere draws with one [DrawMeshAtPoints] connected to the 32 by 16 sphere and a [GridPoints] with counts (129, 1, 1), zero Size and center at the origin. Set the draw size to 0.2 and blending to None. Connect the cube directly to its [DrawMesh], set Stretch to (1.2, 1.2, 0.25) and Center to (0, 0, 2). Compare the preview with camera culling off and on. Move the wall to (100, 0, 2) and repeat, then move the wall draw to the end of the command list and check both positions again.

**Expected:** Both orders report 136,236 input triangles (129 × 1,056 plus 12). On Vulkan the centered wall hides every instance and submits 12 material triangles. Moving the wall outside the frustum exposes all 129 instances and submits 136,224 triangles. In each case the enabled and disabled images match. Direct3D 11 reports 136,236 material triangles at both positions.
