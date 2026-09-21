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
///
/// The ray target is only <b>enabled while this object is editable in the active mode</b> (collision
/// boxes in portal edit mode, MATs in MAT edit mode). Otherwise it is disabled so the reticle passes
/// straight through — e.g. while editing MATs the cursor must be able to reach a MAT that sits behind
/// or inside a portal, instead of snapping onto the portal box in front of it.
/// </summary>
[DisallowMultipleComponent]
public class ReticleSelectable : MonoBehaviour
{
    private RayInteractable _interactable;
    private EditModeManager _editModeManager;
    private bool _isCollision;
    private bool _isMat;
    private bool _modeSubscribed;

    private void Start()
    {
        EnsureRayTarget();
        BindManager();
        ApplyActiveState();
    }

    private void OnEnable()
    {
        // Start may not have run yet on the first enable; both paths are idempotent.
        EnsureRayTarget();
        BindManager();
        ApplyActiveState();
    }

    private void OnDisable()
    {
        UnbindManager();
    }

    private void EnsureRayTarget()
    {
        if (_interactable != null)
        {
            return;
        }

        _isCollision = GetComponent<CollisionObjectBinding>() != null;
        _isMat = GetComponent<MatActionObjectBinding>() != null;

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

    private void BindManager()
    {
        if (_modeSubscribed)
        {
            return;
        }

        _editModeManager = EditModeManager.Instance;
        if (_editModeManager == null)
        {
            return;
        }

        _editModeManager.EditModeChanged += OnModeChanged;
        _editModeManager.MatEditModeChanged += OnModeChanged;
        _modeSubscribed = true;
    }

    private void UnbindManager()
    {
        if (_editModeManager != null && _modeSubscribed)
        {
            _editModeManager.EditModeChanged -= OnModeChanged;
            _editModeManager.MatEditModeChanged -= OnModeChanged;
        }

        _modeSubscribed = false;
    }

    private void OnModeChanged(bool _)
    {
        ApplyActiveState();
    }

    // Enable the reticle ray target only in the mode where this object is actually selectable, so it
    // never blocks the cursor from reaching objects behind it in the other mode.
    private void ApplyActiveState()
    {
        if (_interactable == null)
        {
            return;
        }

        bool selectable = _editModeManager != null &&
                          ((_isCollision && _editModeManager.IsEditMode) ||
                           (_isMat && _editModeManager.IsMatEditMode));
        _interactable.enabled = selectable;
    }

    private void OnDestroy()
    {
        if (_interactable != null)
        {
            _interactable.WhenPointerEventRaised -= OnPointerEventRaised;
        }

        UnbindManager();
    }

    private void OnPointerEventRaised(PointerEvent evt)
    {
        if (evt.Type != PointerEventType.Select)
        {
            return;
        }

        if (_editModeManager == null)
        {
            return;
        }

        // Only claim the selection when this object is editable in the mode that is currently
        // active, mirroring EditModeManager.ResolveSelectable: collision boxes in edit mode, MATs in
        // MAT edit mode. (The ray target is normally disabled outside that mode anyway.)
        bool selectable = (_isCollision && _editModeManager.IsEditMode) ||
                          (_isMat && _editModeManager.IsMatEditMode);
        if (selectable)
        {
            _editModeManager.SetSelectedObject(gameObject);
        }
    }
}
