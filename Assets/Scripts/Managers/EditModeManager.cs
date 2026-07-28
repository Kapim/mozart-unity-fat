using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-900)]
public class EditModeManager : Singleton<EditModeManager>
{
    [SerializeField] private bool startInEditMode;
    [Header("Selection")]
    [Tooltip("Max length of the right-controller ray used to pick an object to manipulate.")]
    [SerializeField] private float selectionRayLength = 20f;

    public bool IsEditMode { get; private set; }
    public bool IsMatEditMode { get; private set; }
    public bool IsAnyEditMode => IsEditMode || IsMatEditMode;
    public GameObject SelectedObject { get; private set; }

    public event Action<bool> EditModeChanged;
    public event Action<bool> MatEditModeChanged;
    public event Action<GameObject> SelectedObjectChanged;

    private void Awake()
    {
        EnsureCollisionDeleteWidgetExists();
        RegisterExistingEditableObjects();
        SetEditMode(startInEditMode);
    }

    private void Update()
    {
#if UNITY_EDITOR
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.cKey.wasPressedThisFrame)
            {
                ToggleEditMode();
            }

            if (keyboard.mKey.wasPressedThisFrame)
            {
                ToggleMatEditMode();
            }
        }
#endif
#endif

        if (!IsAnyEditMode)
        {
            return;
        }

        // Point the right controller at an object and pull the index trigger to select it. The
        // actual move/rotate/scale is then done by the ObjectManipulator on the selected object
        // (unified grip-grab, same as portal placement).
        UpdateRaySelection();
    }

    private void UpdateRaySelection()
    {
        if (!OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
        {
            return;
        }

        Ray ray = BuildRightControllerRay();
        RaycastHit[] hits = Physics.RaycastAll(ray, selectionRayLength, ~0, QueryTriggerInteraction.Collide);
        if (hits != null && hits.Length > 0)
        {
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                // If the ray hits the delete widget first, let it handle its own buttons instead of
                // grabbing an object behind them.
                if (hits[i].collider != null &&
                    hits[i].collider.GetComponentInParent<CollisionBoxDeleteWidget>() != null)
                {
                    return;
                }

                GameObject selectable = ResolveSelectable(hits[i].collider);
                if (selectable != null)
                {
                    SelectAndGrab(selectable);
                    return;
                }
            }
        }
    }

    private void SelectAndGrab(GameObject target)
    {
        SetSelectedObject(target);

        // The same trigger press also starts a ray grab, so pointing + holding the trigger grabs
        // and moves the object (matching the near grip grab).
        var manipulator = target.GetComponent<ObjectManipulator>();
        if (manipulator != null)
        {
            manipulator.BeginTriggerGrab();
        }
    }

    // Returns the manipulable object for the current mode that owns this collider, or null.
    private GameObject ResolveSelectable(Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        if (IsEditMode)
        {
            var binding = collider.GetComponentInParent<CollisionObjectBinding>();
            return binding != null ? binding.gameObject : null;
        }

        if (IsMatEditMode)
        {
            var binding = collider.GetComponentInParent<MatActionObjectBinding>();
            return binding != null ? binding.gameObject : null;
        }

        return null;
    }

    public void ToggleEditMode()
    {
        SetEditMode(!IsEditMode);
    }

    public void SetEditMode(bool enabled)
    {
        if (IsEditMode == enabled)
        {
            return;
        }

        if (enabled && IsMatEditMode)
        {
            SetMatEditModeInternal(false);
        }

        SetEditModeInternal(enabled);
    }

    public void ToggleMatEditMode()
    {
        SetMatEditMode(!IsMatEditMode);
    }

    public void SetMatEditMode(bool enabled)
    {
        if (IsMatEditMode == enabled)
        {
            return;
        }

        if (enabled && IsEditMode)
        {
            SetEditModeInternal(false);
        }

        SetMatEditModeInternal(enabled);
    }

    public void SetSelectedObject(GameObject selected)
    {
        if (SelectedObject == selected)
        {
            return;
        }

        // Stop manipulating the previously selected object (this also flushes any pending change).
        if (SelectedObject != null)
        {
            var previous = SelectedObject.GetComponent<ObjectManipulator>();
            if (previous != null)
            {
                previous.enabled = false;
            }
        }

        SelectedObject = selected;

        // Enable unified grip-grab manipulation on the newly selected object. Scale is only
        // offered for objects that support it (portals), never for MATs.
        if (SelectedObject != null)
        {
            var manipulator = SelectedObject.GetComponent<ObjectManipulator>();
            if (manipulator == null)
            {
                manipulator = SelectedObject.AddComponent<ObjectManipulator>();
            }

            var binding = SelectedObject.GetComponent<IObjectServerBinding>();
            manipulator.AllowScale = binding != null && binding.SupportsScale;
            manipulator.enabled = true;
        }

        SelectedObjectChanged?.Invoke(SelectedObject);
    }

    public EditableObject RegisterEditable(GameObject target)
    {
        if (target == null)
        {
            return null;
        }

        var editable = target.GetComponent<EditableObject>();
        if (editable == null)
        {
            editable = target.AddComponent<EditableObject>();
        }

        editable.SetRestrictManipulationToEditMode(true);

        // Portals (collision boxes) are moved with the unified ObjectManipulator (grip grab), not the
        // Meta ISDK grab rig - exactly like MATs. Their only auto-discovered "Interactable" is the
        // reticle ray target added by ReticleSelectable, whose enabled state must be owned solely by
        // ReticleSelectable (on only in portal edit mode). Without this, EditableObject re-enables it
        // whenever ANY edit mode is active, so the reticle would snap onto portals during object
        // editing and block picking the object inside/behind them. MATs neuter this the same way via
        // ActionObject.DisableMetaGrabBehaviours.
        if (target.GetComponent<CollisionObjectBinding>() != null)
        {
            editable.DisableManipulationManagement();
        }

        editable.ApplyEditMode(IsAnyEditMode);
        return editable;
    }

    private void RegisterExistingEditableObjects()
    {
        var actionObjects = FindObjectsOfType<ActionObject>(true);
        for (int i = 0; i < actionObjects.Length; i++)
        {
            if (actionObjects[i] != null)
            {
                RegisterEditable(actionObjects[i].gameObject);
            }
        }
    }

    private void RefreshEditableRegistrations()
    {
        var editableObjects = FindObjectsOfType<EditableObject>(true);
        for (int i = 0; i < editableObjects.Length; i++)
        {
            if (editableObjects[i] != null)
            {
                editableObjects[i].RefreshManipulationBehaviours();
            }
        }

        var collisionBindings = FindObjectsOfType<CollisionObjectBinding>(true);
        for (int i = 0; i < collisionBindings.Length; i++)
        {
            if (collisionBindings[i] != null)
            {
                if (collisionBindings[i].GetComponent<CollisionBoxEditOverlay>() == null)
                {
                    collisionBindings[i].gameObject.AddComponent<CollisionBoxEditOverlay>();
                }

                if (collisionBindings[i].GetComponent<SelectionWireframe>() == null)
                {
                    collisionBindings[i].gameObject.AddComponent<SelectionWireframe>();
                }

                // Same reticle target MATs/walls carry, so the cursor can highlight and pick portals.
                if (collisionBindings[i].GetComponent<ReticleSelectable>() == null)
                {
                    collisionBindings[i].gameObject.AddComponent<ReticleSelectable>();
                }

                RegisterEditable(collisionBindings[i].gameObject);
            }
        }
    }

    private void SetEditModeInternal(bool enabled)
    {
        if (IsEditMode == enabled)
        {
            return;
        }

        IsEditMode = enabled;
        RefreshEditableRegistrations();
        if (!enabled)
        {
            SetSelectedObject(null);
        }

        EditModeChanged?.Invoke(IsEditMode);
    }

    private void SetMatEditModeInternal(bool enabled)
    {
        if (IsMatEditMode == enabled)
        {
            return;
        }

        IsMatEditMode = enabled;
        RefreshEditableRegistrations();
        if (!enabled)
        {
            SetSelectedObject(null);
        }

        MatEditModeChanged?.Invoke(IsMatEditMode);
    }

    private Ray BuildRightControllerRay()
    {
        Vector3 controllerPosition = TransformControllerLocalToWorld(
            OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch));
        Quaternion controllerRotation = TransformControllerLocalToWorld(
            OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch));
        return new Ray(controllerPosition, controllerRotation * Vector3.forward);
    }

    private Vector3 TransformControllerLocalToWorld(Vector3 localPosition)
    {
        Transform trackingSpace = FindTrackingSpaceTransform();
        return trackingSpace.TransformPoint(localPosition);
    }

    private Quaternion TransformControllerLocalToWorld(Quaternion localRotation)
    {
        Transform trackingSpace = FindTrackingSpaceTransform();
        return trackingSpace.rotation * localRotation;
    }

    private void EnsureCollisionDeleteWidgetExists()
    {
        if (FindFirstObjectByType<CollisionBoxDeleteWidget>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        // Top-level, NOT parented under this manager: its host object may carry a non-identity
        // scale, which the widget (positioned in world space) must not inherit.
        var widgetObject = new GameObject("CollisionBoxDeleteWidget");
        // Bind the widget to THIS manager explicitly. It is created during our own Awake, so it
        // cannot rely on EditModeManager.Instance yet (the Singleton resolves lazily and would
        // otherwise leave the widget unsubscribed, so its delete UI never shows).
        var widget = widgetObject.AddComponent<CollisionBoxDeleteWidget>();
        widget.Initialize(this);
    }

    private Transform FindTrackingSpaceTransform()
    {
        var cameraRig = FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig != null && cameraRig.trackingSpace != null)
        {
            return cameraRig.trackingSpace;
        }

        if (Camera.main != null && Camera.main.transform.parent != null)
        {
            return Camera.main.transform.parent;
        }

        return Camera.main != null ? Camera.main.transform : transform;
    }
}
