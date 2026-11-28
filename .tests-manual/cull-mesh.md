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

## Step: Camera toggle and render statistics

**Action:** Pin Preview and open Performance. With Sphere mesh set to 32 by 16 segments, disable Mesh Occlusion Culling on Camera. Move the wall using TransformMesh. Enable the toggle, set Translation X to -1.55 to cover the sphere, then to 100 to move the wall outside the camera. Allow several frames for GPU readback.

**Expected:**
- With the toggle off, Triangles reads 1,068 and the example issues one draw. The Triangles drawn row is hidden.
- On Vulkan, Triangles stays at 1,068. Triangles drawn reads 40 when covered and 1,056 with the wall outside the camera. A frame capture shows a private depth draw and an indirect material draw. Groups touching visible geometry can include hidden triangles.
- Toggle back and forth. The rendered silhouette matches between modes and the drawn row disappears when disabled.

## Step: Camera, topology and multisampling

**Action:** With the wall outside the view, change Sphere mesh to 8 by 4 segments. Pan both objects outside the view. Repeat at 1 and 4 samples on hardware supporting 4x MSAA.

**Expected:**
- The input count changes to 84. On Vulkan the drawn count settles at 72 with the sphere visible and zero when both objects are outside the view.
- Moving the wall or camera reveals newly visible geometry. Check the silhouette near the wall edge against culling disabled.

## Step: Separate material draws

**Action:** Connect the transformed wall and sphere to separate opaque DrawMesh nodes. Feed their commands into Execute, wall first, and connect Execute to Camera. Compare the preview with the camera toggle off and on. Move the wall from centered to outside the view.

**Expected:** Both draws contribute to the same 1,068 input triangles. Their images match between modes. The sphere contributes no material triangles behind the centered wall and 1,056 with the wall outside the view.

## Step: Direct3D 11 fallback

**Action:** Repeat the toggle, occlusion and topology steps with the Direct3D 11 backend.

**Expected:**
- One direct draw renders the example. With the toggle enabled both triangle counts equal the input, including when both meshes are outside the camera. Disabling the toggle hides Triangles drawn.
- The console reports no shader errors.
