# Edit Mode, Object Selection and Deletion

## Overview

Runtime action objects (Portal Windows / collision boxes and MATs) can be **moved, scaled and
deleted in-headset** while an edit mode is active. This is coordinated by:

- `Assets/Scripts/Managers/EditModeManager.cs`

For how these objects are spawned, see [Runtime Object Lifecycle](runtime-object-lifecycle.md).

## Edit modes

`EditModeManager` exposes two mutually-exclusive modes plus a combined flag:

- **`IsEditMode`** — portal / collision-box editing.
- **`IsMatEditMode`** — MAT editing.
- **`IsAnyEditMode`** — either of the above.

In the Editor the modes can be toggled with the `C` (edit) and `M` (MAT edit) keys; in-headset
they are driven from the menu. Entering a mode registers the editable objects and enables their
edit visuals; leaving a mode clears the current selection.

## Selection (unified on the reticle)

Selection is aimed with the **controller reticle** — the same white cursor that already highlighted
MATs. Every editable object carries a Meta Interaction ray target so the reticle can land on it:

- `Assets/Scripts/Managers/ReticleSelectable.cs` wraps an object's collider in a
  `ColliderSurface` + `RayInteractable` at runtime (no `Grabbable` — a ray *target* only) and
  forwards the reticle's `Select` event to `EditModeManager.SetSelectedObject`.

`PortalMask.prefab` is a bare `BoxCollider`, so before this it could only be picked via a hidden
controller ray; `ReticleSelectable` makes portal boxes selectable by the same cursor as MATs.
`EditModeManager.UpdateRaySelection` still does an equivalent physics ray as a fallback.

Selection feedback:

- `SelectionWireframe.cs` — an orange wireframe around the currently selected object.
- `CollisionBoxEditOverlay.cs` — a translucent blue box over every editable collision box while
  portal edit mode is active.

## Manipulation

The selected object is moved/scaled by:

- `Assets/Scripts/Managers/ObjectManipulator.cs`

One shared control model for portals, MATs and the portal placement preview:

- **Grip (hand trigger)** — the object rigidly follows that controller (grab-move + rotate).
- **Index trigger** — a distance "ray grab" armed by `EditModeManager` when you select an object.
- **Thumbsticks** — per-axis scale, portals only (`AllowScale`); MATs are pose-only.

After a gesture the change is pushed to ARCOR2 through the object's `IObjectServerBinding`.

## Deletion — the delete widget

Deletion uses a small floating widget that appears next to the selected object:

- `Assets/Scripts/Managers/CollisionBoxDeleteWidget.cs`

Behaviour:

- Shows whenever an object with an `IObjectServerBinding` is selected in **any** edit mode, floating
  above-and-to-the-right of the object's bounds and turned to face the viewer.
- Two-step confirm: click 🗑 **Delete** → it becomes a green ✓ (confirm) and a red ✗ (cancel)
  appears → click ✓ to delete or ✗ to abort. It auto-disarms after `confirmTimeoutSeconds`.
- On confirm it calls `IObjectServerBinding.RemoveAsync` (removes the action object on the ARCOR2
  server) and then destroys the local GameObject.

### Server binding contract

`Assets/Scripts/Managers/IObjectServerBinding.cs` is the shared contract so the widget (and
`ObjectManipulator`) work on either object type without knowing the concrete class:

| Binding | Object | Scale | Delete |
|---|---|---|---|
| `CollisionObjectBinding` | Portal / collision box | yes | `RemoveAsync` |
| `MatActionObjectBinding` | MAT | no | `RemoveAsync` |

## Implementation notes and gotchas

These are deliberate and easy to regress:

- **The widget is bound explicitly, not via the singleton.** `EditModeManager` creates the widget
  during its own `Awake` and calls `widget.Initialize(this)`. It must **not** rely on
  `EditModeManager.Instance` in `OnEnable` — the `Singleton` resolves lazily and is not reliably
  available that early, which previously left the widget unsubscribed so it never appeared.
- **The widget host is a top-level object.** It is positioned in world space each `LateUpdate`, so
  it must not inherit a non-identity scale from the manager's GameObject; otherwise the buttons
  become huge or invisibly small.
- **The widget renders on the Default layer (0), never layer `8`.** Layer 8 ("content", legacy) is
  entangled with the portal stencil pipeline, which masked the widget out so it never rendered. See
  [Render Pipeline and Stencil](render-pipeline-and-stencil.md).
- **Buttons are reticle-clickable.** Each button panel gets a `ColliderSurface` + `RayInteractable`
  (same pattern as `ReticleSelectable`) and reacts to the reticle `Select` event — it does **not**
  read `OVRInput` directly, so the click aim matches the visible cursor.

### Button pictograms

The button icons are PNG pictograms loaded at runtime (the widget is created in code, so
Inspector-assigned textures are not an option):

- Location: `Assets/Resources/DeleteWidgetIcons/` — `trash.png`, `check.png`, `cancel.png`.
- They are drawn on transparent, double-sided quads. Because the widget is turned to face the
  viewer (which mirrors X), the texture U is flipped (`flipIconsHorizontally`).
- If a PNG is missing, the widget falls back to a built-in procedural shape and logs a warning with
  the expected path.
