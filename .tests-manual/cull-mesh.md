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

## Step: Frustum and static bounds on Vulkan

**Action:**
Move the Occluder cube to X = 100. Leave the Candidate at the origin with 32 × 16 sphere segments. Capture a frame. Pan the Camera by adding 100 to X on both Position and Target, capture again, then restore both values.

**Expected:**
- The detailed sphere disappears and returns. The candidate indirect draw has a positive vertex count at the origin and zero vertices outside the view.
- Pan slowly until the sphere crosses the camera edge. The visible portion reaches the edge without holes or early disappearance.

## Step: Opaque depth and multisampling

**Action:**
Keep the Candidate at the origin. Set the Occluder center to (0, 0, 2) and capture a frame. Move the wall sideways until part of the sphere appears, then move it back to X = 100. Repeat with the render target set to 4 samples on a device that supports 4x MSAA.

**Expected:**
- The wall stays visible. Full coverage reduces the candidate indirect vertex count below the uncovered count. Moving the wall away restores the sphere and its original count.
- Where the wall partially covers the sphere, uncovered pixels remain visible at its edge at both sample settings.

## Step: Changing geometry and small meshes

**Action:**
With the wall out of view, turn off StaticMesh. Set Candidate Segments to 8 × 4, change Radius, and pan the camera off the sphere and back. Repeat at 32 × 16.

**Expected:**
- Both meshes follow the edits on the next rendered frame. Neither leaves stale geometry behind.
- The 8 × 4 sphere uses the small compute path. Its indirect vertex count is 216 when the whole sphere is inside the view. Both meshes submit zero vertices outside.

## Step: Direct3D 11 fallback on Windows

**Action:**
Open the same example on Direct3D 11. Move the wall from X = 100 to X = 0 and back. Pan the camera across the sphere. Compare the preview to Vulkan at the same positions.

**Expected:**
- The wall covers the sphere and reveals it again. Clipping at the camera edge matches ordinary mesh drawing.
- A frame capture shows a direct candidate draw. The GPU culling kernels and indirect draw are bypassed. The console reports no shader errors for the example.
