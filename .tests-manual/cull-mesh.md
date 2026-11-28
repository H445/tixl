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
Move the Occluder cube to X = 100. Leave the Candidate at the origin with 12 subdivisions per axis. Capture a frame. Move the Candidate to X = 100, capture again, then return it to the origin.

**Expected:**
- The detailed cube disappears and returns. The candidate indirect draw has a positive vertex count at the origin and zero vertices outside the view.
- Move the candidate slowly across the camera edge. The visible portion reaches the edge without holes or early disappearance.

## Step: Opaque depth and multisampling

**Action:**
Keep the Candidate at the origin. Set the Occluder center to (0, 0, 2) and capture a frame. Move the wall sideways until part of the cube appears, then move it back to X = 100. Repeat with the render target set to 4 samples on a device that supports 4x MSAA.

**Expected:**
- The wall stays visible. Full coverage reduces the candidate indirect vertex count below the uncovered count. Moving the wall away restores the cube and its original count.
- Where the wall partially covers the cube, uncovered pixels remain visible at its edge at both sample settings.

## Step: Changing geometry and small meshes

**Action:**
With the wall out of view, turn off StaticMesh. Change Candidate subdivisions from 12 to 1 on each axis, change its size, and move it outside and back inside the view. Repeat at 12 subdivisions.

**Expected:**
- Both meshes follow the edits on the next rendered frame. Neither leaves stale geometry behind.
- The one-segment cube uses the small compute path. All 12 triangles are submitted when fully inside the view. Both meshes submit zero vertices outside.

## Step: Direct3D 11 fallback on Windows

**Action:**
Open the same example on Direct3D 11. Move the wall from X = 100 to X = 0 and back. Move the candidate across the camera edge. Compare the preview to Vulkan at the same positions.

**Expected:**
- The wall covers the cube and reveals it again. Clipping at the camera edge matches ordinary mesh drawing.
- A frame capture shows a direct candidate draw. The GPU culling kernels and indirect draw are bypassed. The console reports no shader errors for the example.
