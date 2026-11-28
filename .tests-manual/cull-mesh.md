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

## Step: Mesh-only occlusion

**Action:**
In [CullMeshExample], trace Sphere mesh and Wall mesh to the Mesh input on [CullMesh]. Move the wall center from (100, 0, 2) to (0, 0, 2), then slide it sideways until the sphere is partly visible. Repeat after reversing the two mesh connections.

**Expected:**
- Both orders show the sphere uncovered, covered, and partly visible at the same positions.
- A Vulkan capture shows a depth-only draw into a private attachment, depth reduction and visibility dispatches, then an indirect material draw into the original target. The covered material draw submits fewer vertices. Surviving 128-face groups can include some hidden faces.
- Returning the wall to X = 100 restores the sphere. There is no occluder command or draw-order input to configure.

## Step: One connected mesh

**Action:**
Connect both primitives to [CombineMeshes] and connect only its result to [CullMesh]. Repeat the wall movement. Then connect only Sphere mesh directly and pan the camera off it and back.

**Expected:**
- The precombined mesh gives the same image and supports occlusion between its parts.
- An isolated sphere clips correctly at the camera edge, disappears outside the view, and returns without stale geometry.

## Step: Camera, topology and multisampling

**Action:**
Run with 1 and 4 samples on a device that supports 4x MSAA. Change the sphere from 32 by 16 segments to 8 by 4. Disable StaticMesh for geometry that changes in place. Move the wall and camera again.

**Expected:**
- Uncovered pixels at the wall edge remain visible at both sample counts. The color pass renders into the original scene target.
- The edited sphere follows the new silhouette. A frame capture shows depth and material draws with matching sample counts.

## Step: Direct3D 11 fallback on Windows

**Action:**
Open the same example on Direct3D 11 and repeat the wall and camera movement.

**Expected:**
- The wall covers the sphere and reveals it again. Clipping at the camera edge matches ordinary mesh drawing.
- A frame capture shows a direct candidate draw. The GPU culling kernels and indirect draw are bypassed. The console reports no shader errors for the example.

## Step: Render statistics

**Action:**
Move the wall out of view, center the sphere, and inspect Triangles in the render statistics. Compare 8 × 4 and 32 × 16 sphere segments. Pan the sphere out of view on Vulkan and wait several frames at each setting.

**Expected:**
- The sphere adds 72 or 1,056 submitted triangles, respectively, to the other draws. On Vulkan its contribution drops to zero outside the view. Indirect counts follow completed GPU frames.
- On Direct3D 11 the fallback reports the full sphere submission even when the camera clips it.
