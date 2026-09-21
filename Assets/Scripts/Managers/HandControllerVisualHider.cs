using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Hides the Meta Interaction SDK hand and controller <b>meshes</b> (the virtual hands and the
/// rendered controller models) while leaving the interaction ray and reticle intact, so pointing
/// and selecting still work. During the experiment the passthrough shows the participant's real
/// hands/controllers, and the overlaid virtual visuals were found distracting.
///
/// It works by setting <c>ForceOffVisibility = true</c> on every <c>HandVisual</c>,
/// <c>ControllerVisual</c> and <c>OVRControllerVisual</c> component. That is the visuals' own
/// public API for hiding themselves (each disables its renderer/root in its Update), so nothing
/// about the interaction rig is broken. The property is set via reflection - matching how the rest
/// of this project pokes at Meta SDK components by type name (see
/// <c>ActionObject.DisableMetaGrabBehaviours</c>) - so it needs no extra assembly references.
///
/// Self-bootstrapping like <c>ToolMenuRecenter</c> / <c>PerformanceOverlay</c>, so it needs no
/// scene wiring. The visuals can spawn a little after scene load and can be recreated when the user
/// switches between hands and controllers, so it re-applies on a light periodic interval.
/// </summary>
public sealed class HandControllerVisualHider : MonoBehaviour
{
    // Meta visual components that expose a public bool "ForceOffVisibility" to hide themselves.
    private static readonly HashSet<string> VisualTypeNames = new HashSet<string>
    {
        "HandVisual",
        "ControllerVisual",
        "OVRControllerVisual",
    };

    // How often to re-scan for visuals to hide. Cheap (a name check per MonoBehaviour) and only
    // needed to catch visuals created/recreated after startup.
    private const float ReapplyIntervalSeconds = 1.0f;

    private readonly Dictionary<System.Type, PropertyInfo> _forceOffByType =
        new Dictionary<System.Type, PropertyInfo>();

    private float _nextApplyTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<HandControllerVisualHider>() != null)
        {
            return;
        }

        var root = new GameObject("HandControllerVisualHider");
        DontDestroyOnLoad(root);
        root.AddComponent<HandControllerVisualHider>();
    }

    private void OnEnable()
    {
        _nextApplyTime = 0f; // apply as soon as possible
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextApplyTime)
        {
            return;
        }

        _nextApplyTime = Time.unscaledTime + ReapplyIntervalSeconds;
        HideAllVisuals();
    }

    private void HideAllVisuals()
    {
        // Include inactive: hand visuals are inactive while controllers are used and vice versa,
        // but ForceOffVisibility persists on the component regardless of its active state.
        var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var behaviour in behaviours)
        {
            if (behaviour == null)
            {
                continue;
            }

            System.Type type = behaviour.GetType();
            if (!VisualTypeNames.Contains(type.Name))
            {
                continue;
            }

            PropertyInfo forceOff = GetForceOffProperty(type);
            if (forceOff != null)
            {
                forceOff.SetValue(behaviour, true);
            }
        }
    }

    private PropertyInfo GetForceOffProperty(System.Type type)
    {
        if (_forceOffByType.TryGetValue(type, out PropertyInfo cached))
        {
            return cached;
        }

        PropertyInfo prop = type.GetProperty(
            "ForceOffVisibility",
            BindingFlags.Public | BindingFlags.Instance);
        if (prop == null || !prop.CanWrite)
        {
            prop = null;
        }

        _forceOffByType[type] = prop;
        return prop;
    }
}
