using UnityEngine;

/// <summary>
/// Unified grab-based manipulation for portals (placement preview + existing boxes) and MAT
/// objects. This is the single control model shared by all three flows. While the component is
/// enabled:
///   - Hold either GRIP (hand trigger): the object rigidly follows that controller, so moving
///     and rotating the controller moves and rotates the object together (a natural "grab").
///     Right hand wins when both grip.
///   - Per-axis scale on the object's own local axes (only when <see cref="AllowScale"/>):
///       right thumbstick X -> width  (local X)
///       right thumbstick Y -> height (local Y)
///       left thumbstick  Y -> depth  (local Z)
///
/// After a gesture ends, if the object carries an <see cref="IObjectServerBinding"/> the change
/// is pushed to the ARCOR2 server. The placement preview has no binding, so it just moves and
/// GameManager reads its transform on confirm.
///
/// Enable this only on the object currently being manipulated: GameManager keeps it enabled on
/// the placement preview; EditModeManager enables it on the selected object and disables it on
/// deselect. (Name intentionally avoids EditableObject's manipulation keywords so it is not
/// auto-toggled by that component.)
/// </summary>
public class ObjectManipulator : MonoBehaviour
{
    [SerializeField] private float scaleSpeed = 1.0f;
    [SerializeField] private float minSize = 0.05f;
    [SerializeField] private float maxSize = 5.0f;
    [SerializeField] private float gripThreshold = 0.5f;
    [SerializeField] private float stickDeadzone = 0.4f;

    /// <summary>Whether the thumbsticks scale this object. Portals: true. MATs: false.</summary>
    public bool AllowScale = true;

    private OVRInput.Controller _grabController = OVRInput.Controller.None;
    private Vector3 _grabPositionOffset;      // object position relative to the controller (controller space)
    private Quaternion _grabRotationOffset;    // object rotation relative to the controller

    private Transform _cachedTrackingSpace;
    private IObjectServerBinding _binding;
    private bool _dirty;
    private bool _triggerGrabArmed;

    private void OnEnable()
    {
        _binding = GetComponent<IObjectServerBinding>();
        _grabController = OVRInput.Controller.None;
        _triggerGrabArmed = false;
        _dirty = false;
    }

    private void OnDisable()
    {
        // Flush any pending change if we get disabled right after a gesture (e.g. deselected).
        FlushIfDirty();
        _grabController = OVRInput.Controller.None;
        _triggerGrabArmed = false;
    }

    /// <summary>
    /// Starts a right-controller "ray grab" driven by the index trigger. Called by EditModeManager
    /// when the user points at this object and pulls the trigger, so the object can be grabbed and
    /// moved from a distance (in addition to the near grip grab). Stays active until the trigger is
    /// released.
    /// </summary>
    public void BeginTriggerGrab()
    {
        _triggerGrabArmed = true;
        _grabController = OVRInput.Controller.None; // force offset re-capture for the new grab
    }

    private void Update()
    {
        bool manipulating = UpdateGrab();
        if (AllowScale)
        {
            manipulating |= UpdateScale();
        }

        // Once the user stops touching the object, persist the accumulated change.
        if (!manipulating)
        {
            FlushIfDirty();
        }
    }

    private bool UpdateGrab()
    {
        float rightGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        float leftGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch);

        // Right index trigger also grabs, but only after EditModeManager armed it by pointing at
        // this object (a distance "ray grab"). It stays active until the trigger is released.
        bool triggerHeld = OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger);
        if (_triggerGrabArmed && !triggerHeld)
        {
            _triggerGrabArmed = false;
        }

        // Pick / keep an active controller. Grip (near grab) wins; right hand wins ties.
        OVRInput.Controller desired = OVRInput.Controller.None;
        if (rightGrip >= gripThreshold)
        {
            desired = OVRInput.Controller.RTouch;
        }
        else if (leftGrip >= gripThreshold)
        {
            desired = OVRInput.Controller.LTouch;
        }
        else if (_triggerGrabArmed && triggerHeld)
        {
            desired = OVRInput.Controller.RTouch;
        }

        if (desired == OVRInput.Controller.None)
        {
            _grabController = OVRInput.Controller.None;
            return false;
        }

        Vector3 controllerPos = ControllerWorldPosition(desired);
        Quaternion controllerRot = ControllerWorldRotation(desired);

        if (_grabController != desired)
        {
            // Grab just started (or switched hands): remember the object pose relative to the controller.
            _grabController = desired;
            _grabPositionOffset = Quaternion.Inverse(controllerRot) * (transform.position - controllerPos);
            _grabRotationOffset = Quaternion.Inverse(controllerRot) * transform.rotation;
            return true;
        }

        transform.position = controllerPos + controllerRot * _grabPositionOffset;
        transform.rotation = controllerRot * _grabRotationOffset;
        _dirty = true;
        return true;
    }

    private bool UpdateScale()
    {
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);

        bool changed = false;

        // Right stick controls the box "face": only the dominant axis is applied so pushing X
        // (width) does not bleed into Y (height) and vice versa.
        if (Mathf.Abs(rightStick.x) >= Mathf.Abs(rightStick.y))
        {
            changed |= ApplyAxisScale(0, rightStick.x);
        }
        else
        {
            changed |= ApplyAxisScale(1, rightStick.y);
        }

        // Left stick up/down controls depth (local Z).
        changed |= ApplyAxisScale(2, leftStick.y);
        return changed;
    }

    private bool ApplyAxisScale(int axis, float input)
    {
        if (Mathf.Abs(input) <= stickDeadzone)
        {
            return false;
        }

        Vector3 scale = transform.localScale;
        float factor = Mathf.Max(0.01f, 1f + (input * scaleSpeed * Time.deltaTime));
        scale[axis] = Mathf.Clamp(scale[axis] * factor, minSize, maxSize);
        transform.localScale = scale;
        _dirty = true;
        return true;
    }

    private void FlushIfDirty()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        if (_binding != null && GameManager.Instance != null && GameManager.Instance.Origin != null)
        {
            _ = _binding.PersistIfChangedAsync(GameManager.Instance.Origin);
        }
    }

    private Vector3 ControllerWorldPosition(OVRInput.Controller controller)
    {
        Transform trackingSpace = FindTrackingSpaceTransform();
        Vector3 local = OVRInput.GetLocalControllerPosition(controller);
        return trackingSpace != null ? trackingSpace.TransformPoint(local) : local;
    }

    private Quaternion ControllerWorldRotation(OVRInput.Controller controller)
    {
        Transform trackingSpace = FindTrackingSpaceTransform();
        Quaternion local = OVRInput.GetLocalControllerRotation(controller);
        return trackingSpace != null ? trackingSpace.rotation * local : local;
    }

    private Transform FindTrackingSpaceTransform()
    {
        if (_cachedTrackingSpace != null)
        {
            return _cachedTrackingSpace;
        }

        var cameraRig = FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig != null && cameraRig.trackingSpace != null)
        {
            _cachedTrackingSpace = cameraRig.trackingSpace;
        }
        else if (Camera.main != null && Camera.main.transform.parent != null)
        {
            _cachedTrackingSpace = Camera.main.transform.parent;
        }
        else if (Camera.main != null)
        {
            _cachedTrackingSpace = Camera.main.transform;
        }

        return _cachedTrackingSpace;
    }
}
