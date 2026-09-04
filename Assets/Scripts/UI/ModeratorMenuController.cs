using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reveals / hides the moderator ("service") menu on a hidden long-press of the
/// left controller Menu button.
///
/// During the planned experiment the participant only interacts with the main
/// menu (create/manipulate portals, add/edit MATs). All setup-oriented controls
/// (scene switching, mesh alignment, room scanning, origin anchor, silhouette
/// portals, tables/conveyors, ...) live in the moderator menu, which stays
/// hidden until the moderator holds the Menu button for <see cref="longPressSeconds"/>.
///
/// This intentionally does not physically re-parent the buttons: it simply
/// toggles the active state of the moderator UI, so it works whether the
/// moderator controls are grouped under a single container
/// (<see cref="moderatorMenuRoot"/>) or left as individual buttons
/// (<see cref="moderatorObjects"/>).
///
/// Uses the same OVRInput pattern already established by KeyframeCaptureManager.
/// </summary>
public class ModeratorMenuController : MonoBehaviour
{
    [Header("What to show / hide")]
    [Tooltip("Optional single container that holds the moderator-only UI. " +
             "If assigned, it is shown/hidden as a whole.")]
    [SerializeField] private GameObject moderatorMenuRoot;

    [Tooltip("Individual moderator-only buttons/objects to show/hide. Used in " +
             "addition to (or instead of) Moderator Menu Root. Assign the ten " +
             "service buttons here if you don't wrap them in a container.")]
    [SerializeField] private GameObject[] moderatorObjects;

    [Header("Activation")]
    [Tooltip("Hold the toggle button this long (seconds) to reveal/hide the " +
             "moderator menu. Long enough that a participant won't trigger it by accident.")]
    [SerializeField] private float longPressSeconds = 1.5f;

    [Tooltip("Controller button used to toggle the menu. Start = the left-hand " +
             "Menu button, which the participant does not use during the experiment.")]
    [SerializeField] private OVRInput.Button toggleButton = OVRInput.Button.Start;

    [Tooltip("Whether the moderator menu is visible on startup. Leave OFF for experiments.")]
    [SerializeField] private bool visibleOnStart = false;

    [Header("Scroll locking")]
    [Tooltip("Tool-menu ScrollRect(s) that should only scroll while the moderator menu is " +
             "visible. During the experiment the participant sees only a few buttons that fit " +
             "on screen, so scrolling is locked to stop accidental scrolls when poking a button " +
             "with an unsteady hand. When the moderator menu is revealed there are enough " +
             "buttons to warrant scrolling, so it is re-enabled.")]
    [SerializeField] private ScrollRect[] lockScrollWhenHidden;

    private bool _visible;

    // >= 0 : accumulating hold time. < 0 : latched (fired, waiting for release).
    private float _heldFor;

    private void Start()
    {
        _visible = visibleOnStart;
        ApplyVisibility();
    }

    private void Update()
    {
        if (OVRInput.Get(toggleButton))
        {
            if (_heldFor >= 0f)
            {
                _heldFor += Time.unscaledDeltaTime;
                if (_heldFor >= longPressSeconds)
                {
                    Toggle();
                    _heldFor = -1f; // latch until the button is released
                }
            }
        }
        else
        {
            _heldFor = 0f;
        }
    }

    /// <summary>Flips moderator-menu visibility. Also usable from a UI button.</summary>
    public void Toggle()
    {
        SetVisible(!_visible);
    }

    /// <summary>Explicitly show or hide the moderator menu.</summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        ApplyVisibility();
    }

    public bool IsVisible => _visible;

    private void ApplyVisibility()
    {
        if (moderatorMenuRoot != null)
        {
            moderatorMenuRoot.SetActive(_visible);
        }

        if (moderatorObjects != null)
        {
            foreach (var go in moderatorObjects)
            {
                if (go != null)
                {
                    go.SetActive(_visible);
                }
            }
        }

        ApplyScrollLock();
    }

    /// <summary>
    /// Locks vertical/horizontal scrolling on the tool menu while the moderator menu is hidden.
    /// When locking, the content is snapped back to the top so every participant button stays in
    /// view. Leaving the ScrollRect components enabled (rather than disabling them) keeps their
    /// masking/layout intact.
    /// </summary>
    private void ApplyScrollLock()
    {
        if (lockScrollWhenHidden == null)
        {
            return;
        }

        foreach (var scroll in lockScrollWhenHidden)
        {
            if (scroll == null)
            {
                continue;
            }

            scroll.horizontal = _visible;
            scroll.vertical = _visible;

            if (!_visible)
            {
                // Snap back to the top and stop any residual momentum so the locked menu
                // does not stay mid-scroll with buttons pushed out of view.
                scroll.velocity = Vector2.zero;
                scroll.verticalNormalizedPosition = 1f;
            }
        }
    }
}
