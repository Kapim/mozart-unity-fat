using UnityEngine;

/// <summary>
/// Lets the user freely place a portal bounding box (including in mid-air) before it is
/// committed as a virtual collision object.
///
/// Interaction (self-contained, no dependency on EditModeManager selection — the preview
/// box is not yet an ARCOR2 object):
///   - Hold either GRIP (hand trigger): the box rigidly follows that controller, so moving
///     and rotating the controller moves and rotates the box together (a natural "grab").
///   - Per-axis scale on the box's own local axes:
///       right thumbstick X -> width  (local X)
///       right thumbstick Y -> height (local Y)
///       left thumbstick  Y -> depth  (local Z)
/// </summary>
public class PortalPlacementBox : MonoBehaviour
{
    [SerializeField] private float scaleSpeed = 1.0f;
    [SerializeField] private float minSize = 0.05f;
    [SerializeField] private float maxSize = 5.0f;
    [SerializeField] private float gripThreshold = 0.5f;
    [SerializeField] private float stickDeadzone = 0.4f;

    private OVRInput.Controller _grabController = OVRInput.Controller.None;
    private Vector3 _grabPositionOffset;      // box position relative to the controller (controller space)
    private Quaternion _grabRotationOffset;    // box rotation relative to the controller

    private Transform _cachedTrackingSpace;

    private void Update()
    {
        UpdateGrab();
        UpdateScale();
    }

    private void UpdateGrab()
    {
        float rightGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        float leftGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch);

        // Pick / keep an active controller. Right hand wins when both are gripping.
        OVRInput.Controller desired = OVRInput.Controller.None;
        if (rightGrip >= gripThreshold)
        {
            desired = OVRInput.Controller.RTouch;
        }
        else if (leftGrip >= gripThreshold)
        {
            desired = OVRInput.Controller.LTouch;
        }

        if (desired == OVRInput.Controller.None)
        {
            _grabController = OVRInput.Controller.None;
            return;
        }

        Vector3 controllerPos = ControllerWorldPosition(desired);
        Quaternion controllerRot = ControllerWorldRotation(desired);

        if (_grabController != desired)
        {
            // Grab just started (or switched hands): remember the box pose relative to the controller.
            _grabController = desired;
            _grabPositionOffset = Quaternion.Inverse(controllerRot) * (transform.position - controllerPos);
            _grabRotationOffset = Quaternion.Inverse(controllerRot) * transform.rotation;
            return;
        }

        transform.position = controllerPos + controllerRot * _grabPositionOffset;
        transform.rotation = controllerRot * _grabRotationOffset;
    }

    private void UpdateScale()
    {
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);

        // Right stick controls the box "face": only the dominant axis is applied so pushing X
        // (width) does not bleed into Y (height) and vice versa.
        if (Mathf.Abs(rightStick.x) >= Mathf.Abs(rightStick.y))
        {
            ApplyAxisScale(0, rightStick.x);
        }
        else
        {
            ApplyAxisScale(1, rightStick.y);
        }

        // Left stick up/down controls depth (local Z).
        ApplyAxisScale(2, leftStick.y);
    }

    private void ApplyAxisScale(int axis, float input)
    {
        if (Mathf.Abs(input) <= stickDeadzone)
        {
            return;
        }

        Vector3 scale = transform.localScale;
        float factor = Mathf.Max(0.01f, 1f + (input * scaleSpeed * Time.deltaTime));
        scale[axis] = Mathf.Clamp(scale[axis] * factor, minSize, maxSize);
        transform.localScale = scale;
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
