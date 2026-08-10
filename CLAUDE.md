# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this project is

**MOZART Feasibility Analysis Tool** — a Unity **Meta Quest 3** mixed-reality app (part of the
[MOZART project](https://mozart-robotics.eu/)) for presenting and evaluating robotic food
packaging / handling scenarios in a real production environment. It uses a **Diminished Reality**
approach: the real room stays visible through passthrough, virtual industrial content is rendered
on top, and selected real surfaces are visually removed/replaced. A **Portal Window** can reveal
an *Alternate Scene* representation of the room.

- Engine: **Unity 6000.3.10f1** (URP 17.3.0), Android build target (Quest 3).
- Package id: `cz.fitvut.fat`.
- Key packages: Meta XR SDK 85.0.0 (`com.meta.xr.sdk.all`), OpenXR 1.16.1 + Meta OpenXR 2.5.0,
  Input System, AI Navigation.

## Documentation is the source of truth — read it first

Technical docs live in **`Docs/`** and are the primary reference. Start with
[`Docs/index.md`](Docs/index.md). Recommended order: `glossary.md` → `setup-and-external-dependencies.md`
→ `architecture-overview.md` → `diminished-reality.md` → `render-pipeline-and-stencil.md` →
`runtime-object-lifecycle.md` → `debugging-and-profiling.md` → `adr/adr-001-portal-rendering-on-quest.md`.

Also read [`README.md`](README.md) for the newer features (object-shaped portals, room scanning,
MRUK pipeline). **Keep `Docs/` and `README.md` in sync with code changes** — architectural decisions
are versioned alongside the code here.

## Code layout (`Assets/Scripts/`)

- `Managers/` — runtime coordination. `GameManager.cs` is the central coordinator (scene/action
  object lifecycle, Alternate Scene mesh, portal materials). `CommunicationManager.cs` talks to the
  ARCOR2 backend over websocket. `AlwaysVisibleContentRenderer.cs` handles always-visible content.
- `Mesh/` — scene mesh + segmentation. `MeshDownloadManager.cs` (mesh HTTP service),
  `MeshImporter.cs` (needs TriLib/SimpleCollada), `LayerApplier.cs`, `GlobalMeshProvider.cs`,
  `MeshSegmentationClient.cs`, `ScanRoomFlow.cs`, `PortalBoxBinder.cs`, `BackgroundMesh.cs`.
- `Core/` — `Singleton.cs`, `MainThreadDispatcher.cs`, `ActionObject.cs`.
- `Debug/` — diagnostics & capture: `ObjectPicker.cs` (per-cluster portal picking),
  `KeyframeCaptureManager.cs`, `QuestDatasetRecorder.cs`, `EnvDepthProbe.cs`, `PerformanceOverlay.cs`.
- `Environment/`, `UI/`, `Utils/`, `Data/`, and `MozartSpatialBridge.cs` (spatial alignment).

Shaders: `Assets/Materials/Shaders/` (`StencilMask.shader`, `PortalContentUnlit.shader`,
`SelectivePassthroughStencil.shader`, `EnvDepthCapture.shader`). Mobile renderer:
`Assets/Settings/Mobile_Renderer.asset`.

## Rendering — important constraints

- The portal/stencil pipeline is **shader/material-driven**. The project **intentionally avoids URP
  `RenderObjects` / custom renderer features on mobile XR** — they caused frame spikes and XR drift
  on device (see `architecture-overview.md` and ADR-001). Do not reintroduce them without profiling.
- Layers: `6 background`, `7 passthroughWalls`, `8 content` (legacy/deprecated — don't use for new
  paths), `9 portalMask`, `10 portalContent`.
- **Dynamic occlusion status:** `PortalContentUnlit.shader` reconstructs the portal box's front
  face via ray-box intersection (`PortalFrontFaceWorld`, fed by `PortalBoxBinder.cs` →
  `_PortalWorldToLocal`/`_PortalBoxCenter`/`_PortalBoxExtents`) and samples Meta Environment Depth
  there. This **works correctly for box-shaped portals** — real objects in front of the opening
  occlude the portal content. It does **not** work for object-shaped (silhouette) portals, because
  the ray-box math assumes a cuboid. The intended fix is a shape-agnostic depth reconstruction from
  `_CameraDepthTexture`. (Current branch: `dynamic-occlusion`.)

## Object-shaped portals & clusters

Portals can be masked to a real object's silhouette. The per-object "clusters" are produced
**offline** by segmenting a room mesh with `Assets/StreamingAssets/clusters/export_clusters_normals.py`
(RANSAC plane removal + normals-augmented DBSCAN; needs **Python 3.11** + Open3D + NumPy).
Generated cluster files (`clusterN.obj`, `clusters.json`, `centres.txt`) are **not committed** —
recreate them with the script. `ObjectPicker.cs` loads them at runtime. See README "Object-Shaped
Portals" for the full workflow.

An **experimental** live route (MRUK scan → `GlobalMeshProvider` → `MeshSegmentationClient` →
Flask server `tools/segmentation_server/app.py` → `ObjectPicker`) is **disabled by default**
(`ScanRoomFlow.drivePortalsFromScan = false`).

## External dependencies (build will not fully work without them)

- **ARCOR2 backend** (websocket) — configure `CommunicationManager.ServerUri`.
- **mesh-obb-cutter** mesh service (HTTP) — configure `MeshDownloadManager.meshServerBaseUrl`.
- Private Asset Store / 3rdparty packages **not committed**: **TriLib** and **SimpleCollada**
  (required by `MeshImporter.cs`). Obtain from the Robo@FIT private source.
- When testing on device, services must be reachable **from the headset**, not just the dev PC.

## Working conventions

- This is a **Unity project on Windows** — the shell is PowerShell; a Bash tool is also available.
- `.cs` scripts have paired `.meta` files; when adding/moving/deleting scripts, keep `.meta` files
  consistent (Unity manages GUIDs through them).
- Building/running requires the Unity Editor and a connected Quest 3 (Android) — Claude generally
  cannot build/run this itself; make code + doc changes and let the user verify in-editor/on-device.
- Prefer editing existing docs in `Docs/` over creating new top-level markdown.
