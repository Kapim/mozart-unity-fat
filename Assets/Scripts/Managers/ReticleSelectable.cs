using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;

/// <summary>
/// Makes an object selectable by the controller reticle (the white ring "cursor" that already
/// highlights MATs and walls). At runtime it wraps the object's collider in the Meta Interaction
/// SDK ray target (<see cref="ColliderSurface"/> + <see cref="RayInteractable"/>) so the reticle
/// snaps onto it, and forwards the reticle's Select event to
/// <see cref="EditModeManager.SetSelectedObject"/>.
///
/// This unifies portal (collision box) selection with MAT/wall selection: previously only objects
/// carrying the ISDK ray-grab block showed the reticle, so portals (<c>PortalMask.prefab</c>, a bare
/// BoxCollider) could only be picked through the hidden controller ray in
/// <see cref="EditModeManager.UpdateRaySelection"/>. With this component the same cursor picks
/// everything.
///
/// It only adds a ray <i>target</i> (surface + interactable) — no Grabbable, no movement provider —
/// so the reticle merely hovers/selects; moving and scaling stays with <see cref="ObjectManipulator"/>
/// (grip grab), matching how MATs are handled. Added in code rather than baked into the prefab to
/// stay consistent with how GameManager builds collision boxes and to avoid hand-wiring fragile Meta
/// SDK references in prefab YAML.
/// </summary>
[DisallowMultipleComponent]
public class ReticleSelectable : MonoBehaviour
{
    private RayInteractable _interactable;

    private void Start()
    {
        EnsureRayTarget();
    }

    private void EnsureRayTarget()
    {
        if (_interactable != null)
        {
            return;
        }

        // The reticle raycasts against a Meta ISurface; reuse the object's existing collider (the
        // same volume EditModeManager's physics ray already selects) so the two aims agree.
        Collider selectionCollider = GetComponentInChildren<Collider>();
        if (selectionCollider == null)
        {
            Debug.LogWarning(
                $"ReticleSelectable on '{name}' found no collider; the reticle cannot hover it.");
            return;
        }

        GameObject host = selectionCollider.gameObject;

        var surface = host.GetComponent<ColliderSurface>();
        if (surface == null)
        {
            surface = host.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(selectionCollider);
        }

        _interactable = host.GetComponent<RayInteractable>();
        if (_interactable == null)
        {
            _interactable = host.AddComponent<RayInteractable>();
            _interactable.InjectAllRayInteractable(surface);
        }

        _interactable.WhenPointerEventRaised += OnPointerEventRaised;
    }

    private void OnDestroy()
    {
        if (_interactable != null)
        {
            _interactable.WhenPointerEventRaised -= OnPointerEventRaised;
        }
    }

    private void OnPointerEventRaised(PointerEvent evt)
    {
        if (evt.Type != PointerEventType.Select)
        {
            return;
        }

        var editModeManager = EditModeManager.Instance;
        if (editModeManager == null)
        {
            return;
        }

        // Only claim the selection when this object is editable in the mode that is currently
        // active, mirroring EditModeManager.ResolveSelectable: collision boxes in edit mode, MATs in
        // MAT edit mode.
        bool isCollision = GetComponent<CollisionObjectBinding>() != null;
        bool isMat = GetComponent<MatActionObjectBinding>() != null;

        bool selectable = (isCollision && editModeManager.IsEditMode) ||
                          (isMat && editModeManager.IsMatEditMode);
        if (selectable)
        {
            editModeManager.SetSelectedObject(gameObject);
        }
    }
}
