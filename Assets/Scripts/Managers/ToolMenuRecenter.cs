using UnityEngine;

/// <summary>
/// Recenters the main tool menu (<see cref="GameManager.SceneEditorMainMenu"/>) in front of the
/// user on a short press of a left-controller button. The menu occasionally drifts out of view
/// (behind the user, inside a wall, or too far away) and becomes hard to find again; a quick tap
/// brings it back to a comfortable spot facing the headset.
///
/// Self-bootstrapping like <c>PerformanceOverlay</c>, so it needs no scene wiring. It only moves
/// the menu — it does NOT toggle its active state, so a menu that is intentionally hidden (e.g.
/// while the scenes list is shown) stays hidden.
/// </summary>
public sealed class ToolMenuRecenter : MonoBehaviour
{
    // Left-controller Y button. Currently unused elsewhere, so it won't clash with existing input.
    // NOTE: we read the *raw* Y button rather than the virtual OVRInput.Button.Four. The virtual
    // mapping is only defined for the active/combined controller, so pairing it with an explicit
    // Controller.LTouch can silently return false. RawButton.Y is unambiguous for the left Y.
    private const OVRInput.RawButton RecenterButton = OVRInput.RawButton.Y;

    // A press shorter than this counts as a "short" tap; longer holds are ignored so the button
    // stays free for a future long-press action.
    private const float MaxShortPressSeconds = 0.5f;

    // Where to drop the menu relative to the headset when recentering.
    private const float MenuDistanceMeters = 0.7f;
    private const float MenuVerticalOffsetMeters = -0.1f;

    private float _pressStartTime = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<ToolMenuRecenter>() != null)
        {
            return;
        }

        var root = new GameObject("ToolMenuRecenter");
        DontDestroyOnLoad(root);
        root.AddComponent<ToolMenuRecenter>();
    }

    private void Update()
    {
        if (OVRInput.GetDown(RecenterButton))
        {
            _pressStartTime = Time.unscaledTime;
        }

        if (!OVRInput.GetUp(RecenterButton))
        {
            return;
        }

        bool wasShortPress = _pressStartTime >= 0f
                             && (Time.unscaledTime - _pressStartTime) <= MaxShortPressSeconds;
        _pressStartTime = -1f;

        if (wasShortPress)
        {
            RecenterToolMenu();
        }
    }

    private void RecenterToolMenu()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null || gameManager.SceneEditorMainMenu == null)
        {
            return;
        }

        Transform head = ResolveHeadTransform();
        if (head == null)
        {
            return;
        }

        // Keep the placement level with the horizon so the panel never ends up pitched up/down.
        Vector3 flatForward = head.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flatForward = Vector3.forward;
        }
        flatForward.Normalize();

        Transform menu = gameManager.SceneEditorMainMenu.transform;
        menu.position = head.position
                        + flatForward * MenuDistanceMeters
                        + Vector3.up * MenuVerticalOffsetMeters;
        menu.rotation = Quaternion.LookRotation(menu.position - head.position, Vector3.up);
    }

    private static Transform ResolveHeadTransform()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null)
        {
            return rig.centerEyeAnchor;
        }

        return Camera.main != null ? Camera.main.transform : null;
    }
}
