using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.Rendering;

public class CollisionBoxDeleteWidget : MonoBehaviour
{
    [SerializeField] private float widgetVerticalOffset = 0.12f;
    [SerializeField] private float widgetHorizontalOffset = 0.12f;
    [SerializeField] private float buttonWidth = 0.14f;
    [SerializeField] private float buttonHeight = 0.09f;
    [SerializeField] private float buttonDepth = 0.02f;
    [SerializeField] private float buttonSpacing = 0.03f;
    [SerializeField] private float confirmTimeoutSeconds = 3f;
    // Default layer (0), NOT the legacy "content" layer 8 — that layer is entangled with the portal
    // stencil pipeline, which was masking the widget out so it never rendered.
    [SerializeField] private int widgetLayer = 0;
    [SerializeField] private Color deleteColor = new Color(0.80f, 0.20f, 0.18f, 1f);   // red   (trash)
    [SerializeField] private Color confirmColor = new Color(0.20f, 0.68f, 0.32f, 1f);  // green (check)
    [SerializeField] private Color cancelColor = new Color(0.78f, 0.24f, 0.20f, 1f);   // red   (cross)
    [SerializeField] private Color disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
    [SerializeField] private Color iconColor = Color.white;
    [Tooltip("Size (metres) of the square PNG pictogram quads.")]
    [SerializeField] private float iconSize = 0.055f;
    [Tooltip("The widget faces the viewer, which mirrors X; flip the pictograms' U so they read " +
             "correctly. If an icon looks mirrored, toggle this.")]
    [SerializeField] private bool flipIconsHorizontally = true;
    [Tooltip("Print diagnostic logs (visible via adb logcat) explaining when/why the delete " +
             "widget shows or stays hidden. Leave off for normal use.")]
    [SerializeField] private bool verboseLogging = false;

    // PNG pictograms are loaded from Assets/Resources/<IconResourceFolder>/<name>.png at runtime
    // (the widget is created in code, so Inspector-assigned textures are not an option). Missing
    // files fall back to the built-in procedural shapes.
    private const string IconResourceFolder = "DeleteWidgetIcons";

    private EditModeManager _editModeManager;
    private GameObject _root;
    private GameObject _deleteButton;
    private GameObject _cancelButton;
    private Collider _deleteCollider;
    private Collider _cancelCollider;
    private Material _deleteMaterial;
    private Material _cancelMaterial;
    private Material _iconMaterial;
    private readonly List<Material> _iconImageMaterials = new List<Material>();
    private RayInteractable _deleteInteractable;
    private RayInteractable _cancelInteractable;
    private GameObject _deleteTrashIcon;   // shown before arming
    private GameObject _deleteCheckIcon;   // shown after arming (this button becomes "confirm/yes")
    private GameObject _cancelCrossIcon;
    private IObjectServerBinding _selectedBinding;
    private GameObject _selectedObject;
    private bool _subscribed;
    private bool _confirmArmed;
    private float _confirmDeadline;
    private bool _isDeleting;

    /// <summary>
    /// Bind the widget to the manager that owns it. Called by EditModeManager right after this
    /// component is created (during the manager's own Awake), so the widget never depends on the
    /// lazily-resolved EditModeManager.Instance being available yet.
    /// </summary>
    public void Initialize(EditModeManager manager)
    {
        BindManager(manager);
    }

    private void Awake()
    {
        EnsureWidget();
        ApplyVisibility(false);
    }

    private void OnEnable()
    {
        // Prefer the explicitly-injected manager; fall back to the singleton if we were enabled
        // without an Initialize (e.g. re-enabled at runtime).
        BindManager(_editModeManager != null ? _editModeManager : EditModeManager.Instance);
        RefreshSelection();
    }

    private void OnDisable()
    {
        UnbindManager();
        ApplyVisibility(false);
    }

    private void BindManager(EditModeManager manager)
    {
        if (manager == null || (_editModeManager == manager && _subscribed))
        {
            return;
        }

        UnbindManager();
        _editModeManager = manager;
        _editModeManager.EditModeChanged += OnEditModeChanged;
        _editModeManager.MatEditModeChanged += OnEditModeChanged;
        _editModeManager.SelectedObjectChanged += OnSelectedObjectChanged;
        _subscribed = true;
        if (verboseLogging)
        {
            Debug.Log($"[DeleteWidget] Bound to EditModeManager '{_editModeManager.name}'.");
        }
        RefreshSelection();
    }

    private void UnbindManager()
    {
        if (_editModeManager != null && _subscribed)
        {
            _editModeManager.EditModeChanged -= OnEditModeChanged;
            _editModeManager.MatEditModeChanged -= OnEditModeChanged;
            _editModeManager.SelectedObjectChanged -= OnSelectedObjectChanged;
        }

        _subscribed = false;
    }

    private void OnDestroy()
    {
        if (_deleteInteractable != null)
        {
            _deleteInteractable.WhenPointerEventRaised -= OnDeletePointerEvent;
        }

        if (_cancelInteractable != null)
        {
            _cancelInteractable.WhenPointerEventRaised -= OnCancelPointerEvent;
        }

        if (_deleteMaterial != null)
        {
            Destroy(_deleteMaterial);
        }

        if (_cancelMaterial != null)
        {
            Destroy(_cancelMaterial);
        }

        if (_iconMaterial != null)
        {
            Destroy(_iconMaterial);
        }

        foreach (var material in _iconImageMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        _iconImageMaterials.Clear();
    }

    private void Update()
    {
        // Lazily bind if we were created before EditModeManager.Instance was resolvable and no
        // explicit Initialize reached us.
        if (!_subscribed)
        {
            BindManager(_editModeManager != null ? _editModeManager : EditModeManager.Instance);
        }

        if (_confirmArmed && Time.time > _confirmDeadline)
        {
            ResetConfirmationState();
        }

        if (!ShouldShowWidget())
        {
            ApplyVisibility(false);
            return;
        }

        ApplyVisibility(true);
        UpdateButtonVisuals();
    }

    private void LateUpdate()
    {
        if (!ShouldShowWidget())
        {
            return;
        }

        UpdateWidgetTransform();
    }

    private void OnEditModeChanged(bool _)
    {
        RefreshSelection();
    }

    private void OnSelectedObjectChanged(GameObject _)
    {
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        _selectedObject = _editModeManager != null ? _editModeManager.SelectedObject : null;
        // Any server-backed object (portal collision box OR MAT) can be deleted through the same
        // widget now, via the shared IObjectServerBinding.RemoveAsync.
        _selectedBinding = _selectedObject != null
            ? _selectedObject.GetComponent<IObjectServerBinding>()
            : null;

        if (_selectedBinding == null)
        {
            ResetConfirmationState();
        }

        bool show = ShouldShowWidget();
        if (verboseLogging)
        {
            Debug.Log($"[DeleteWidget] RefreshSelection show={show} " +
                      $"mgr={( _editModeManager != null)} subscribed={_subscribed} " +
                      $"anyEdit={(_editModeManager != null && _editModeManager.IsAnyEditMode)} " +
                      $"editMode={(_editModeManager != null && _editModeManager.IsEditMode)} " +
                      $"matMode={(_editModeManager != null && _editModeManager.IsMatEditMode)} " +
                      $"selObj={(_selectedObject != null ? _selectedObject.name : "null")} " +
                      $"binding={(_selectedBinding != null)} " +
                      $"active={(_selectedObject != null && _selectedObject.activeInHierarchy)}");
        }

        ApplyVisibility(show);
    }

    private bool ShouldShowWidget()
    {
        return _editModeManager != null &&
               _editModeManager.IsAnyEditMode &&
               _selectedBinding != null &&
               _selectedObject != null &&
               _selectedObject.activeInHierarchy;
    }

    // The buttons are Meta Interaction ray targets (see SetupButtonRayTarget), so the same reticle
    // cursor that selects portals/MATs also drives them: aim the reticle at a button and pull the
    // trigger to fire its Select, which lands here.
    private void OnDeletePointerEvent(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Select)
        {
            OnDeletePressed();
        }
    }

    private void OnCancelPointerEvent(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Select)
        {
            OnCancelPressed();
        }
    }

    private void OnDeletePressed()
    {
        if (_isDeleting || !ShouldShowWidget())
        {
            return;
        }

        // First press arms the confirmation (trash -> green check + red cross); second confirms.
        if (!_confirmArmed)
        {
            _confirmArmed = true;
            _confirmDeadline = Time.time + confirmTimeoutSeconds;
            UpdateButtonVisuals();
            return;
        }

        _ = DeleteSelectedCollisionObjectAsync();
    }

    private void OnCancelPressed()
    {
        if (_isDeleting)
        {
            return;
        }

        ResetConfirmationState();
    }

    private async Task DeleteSelectedCollisionObjectAsync()
    {
        if (_isDeleting || _selectedBinding == null)
        {
            return;
        }

        _isDeleting = true;
        UpdateButtonVisuals();

        GameObject target = _selectedObject;
        bool removed = await _selectedBinding.RemoveAsync(force: true);
        if (removed)
        {
            _editModeManager?.SetSelectedObject(null);
            if (target != null)
            {
                Destroy(target);
            }
        }

        _isDeleting = false;
        ResetConfirmationState();
        RefreshSelection();
    }

    private void EnsureWidget()
    {
        if (_root != null)
        {
            return;
        }

        // Child of this component's GameObject so EditModeManager's ray-selection guard can find the
        // widget via GetComponentInParent. The host itself is created top-level (see
        // EditModeManager.EnsureCollisionDeleteWidgetExists), so no non-identity scale is inherited.
        _root = new GameObject("CollisionBoxDeleteWidgetRoot");
        _root.transform.SetParent(transform, false);
        _root.transform.localScale = Vector3.one;
        SetLayerRecursively(_root, widgetLayer);

        _iconMaterial = CreateButtonMaterial(iconColor);

        // Delete button: red panel with a trash icon; when armed it becomes the green "confirm/yes"
        // (check icon).
        GameObject deletePanel;
        (_deleteButton, deletePanel, _deleteCollider, _deleteMaterial) =
            CreatePanelButton("DeleteButton", deleteColor, Vector3.zero);
        _deleteTrashIcon = BuildIcon(_deleteButton.transform, "trash", BuildTrashIcon);
        _deleteCheckIcon = BuildIcon(_deleteButton.transform, "check", BuildCheckIcon);
        _deleteInteractable = SetupButtonRayTarget(deletePanel, _deleteCollider);
        if (_deleteInteractable != null)
        {
            _deleteInteractable.WhenPointerEventRaised += OnDeletePointerEvent;
        }

        // Cancel button: red panel with a cross icon; only visible while armed.
        GameObject cancelPanel;
        (_cancelButton, cancelPanel, _cancelCollider, _cancelMaterial) =
            CreatePanelButton("CancelButton", cancelColor, new Vector3(buttonWidth + buttonSpacing, 0f, 0f));
        _cancelCrossIcon = BuildIcon(_cancelButton.transform, "cancel", BuildCrossIcon);
        _cancelInteractable = SetupButtonRayTarget(cancelPanel, _cancelCollider);
        if (_cancelInteractable != null)
        {
            _cancelInteractable.WhenPointerEventRaised += OnCancelPointerEvent;
        }
    }

    // Builds a button as an unscaled root (so icons parented to it stay undistorted) holding a
    // colored "panel" cube. The panel carries the collider used as the reticle ray target.
    private (GameObject root, GameObject panel, Collider collider, Material material)
        CreatePanelButton(string name, Color color, Vector3 localPosition)
    {
        var root = new GameObject(name);
        root.transform.SetParent(_root.transform, false);
        root.transform.localPosition = localPosition;
        SetLayerRecursively(root, widgetLayer);

        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Panel";
        panel.transform.SetParent(root.transform, false);
        panel.transform.localScale = new Vector3(buttonWidth, buttonHeight, buttonDepth);
        SetLayerRecursively(panel, widgetLayer);

        var collider = panel.GetComponent<Collider>();
        var renderer = panel.GetComponent<MeshRenderer>();
        var material = CreateButtonMaterial(color);
        renderer.sharedMaterial = material;

        return (root, panel, collider, material);
    }

    // Wraps a button panel's collider in the Meta Interaction ray target (ColliderSurface +
    // RayInteractable), mirroring ReticleSelectable, so the controller reticle can hover and click
    // it. Only a ray target is added — no Grabbable — so the button just responds to Select.
    private RayInteractable SetupButtonRayTarget(GameObject panel, Collider collider)
    {
        if (panel == null || collider == null)
        {
            return null;
        }

        var surface = panel.AddComponent<ColliderSurface>();
        surface.InjectAllColliderSurface(collider);

        var interactable = panel.AddComponent<RayInteractable>();
        interactable.InjectAllRayInteractable(surface);
        return interactable;
    }

    // Builds an icon: a PNG pictogram quad if the texture exists under
    // Assets/Resources/<IconResourceFolder>/<name>.png, otherwise the built-in procedural shape.
    private GameObject BuildIcon(Transform parent, string resourceName, Func<Transform, GameObject> proceduralFallback)
    {
        var tex = Resources.Load<Texture2D>(IconResourceFolder + "/" + resourceName);
        if (tex == null)
        {
            Debug.LogWarning($"[DeleteWidget] Icon 'Assets/Resources/{IconResourceFolder}/{resourceName}.png' " +
                             "not found — using the built-in shape. Import a PNG there to replace it.");
            return proceduralFallback(parent);
        }

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var quadCollider = quad.GetComponent<Collider>();
        if (quadCollider != null)
        {
            Destroy(quadCollider); // decorative only, never a ray target
        }

        quad.name = resourceName + "Icon";
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = new Vector3(0f, 0f, IconZ);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(iconSize, iconSize, 1f);

        var material = CreateIconImageMaterial(tex);
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        _iconImageMaterials.Add(material);
        SetLayerRecursively(quad, widgetLayer);
        return quad;
    }

    private Material CreateIconImageMaterial(Texture2D tex)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        var m = new Material(shader);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);

        // Transparent so the pictogram's alpha lets the panel colour show through.
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // double-sided: facing never matters
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;

        // The widget is turned to face the viewer, which mirrors X; flip the texture's U so the
        // pictogram reads the right way round.
        if (flipIconsHorizontally)
        {
            var scale = new Vector2(-1f, 1f);
            var offset = new Vector2(1f, 0f);
            if (m.HasProperty("_BaseMap")) { m.SetTextureScale("_BaseMap", scale); m.SetTextureOffset("_BaseMap", offset); }
            if (m.HasProperty("_MainTex")) { m.SetTextureScale("_MainTex", scale); m.SetTextureOffset("_MainTex", offset); }
        }

        return m;
    }

    // ---- Procedural icons (built from thin cubes, so they need no font/emoji glyphs) ----
    // Fallbacks used only when a PNG pictogram is missing. All icons sit just in front of the panel
    // face (+local Z, which UpdateWidgetTransform aims at the viewer) and are parented to the
    // unscaled button root, so they keep their real proportions.

    private float IconZ => buttonDepth * 0.5f + 0.003f;

    private GameObject BuildCheckIcon(Transform parent)
    {
        var root = NewIconRoot("CheckIcon", parent);
        // A check mark = two strokes meeting at a bottom vertex (~ -0.005, -0.012):
        //   short arm going up-left, long arm going up-right. Centres/angles/lengths are computed so
        //   both bars' ends land on that shared vertex.
        MakeBar(root.transform, new Vector3(-0.011f, -0.006f, 0f), 135f, 0.017f, 0.008f); // up-left
        MakeBar(root.transform, new Vector3(0.007f, 0.002f, 0f), 49f, 0.037f, 0.008f);    // up-right
        return root;
    }

    private GameObject BuildCrossIcon(Transform parent)
    {
        var root = NewIconRoot("CrossIcon", parent);
        MakeBar(root.transform, Vector3.zero, 45f, 0.034f, 0.009f);
        MakeBar(root.transform, Vector3.zero, -45f, 0.034f, 0.009f);
        return root;
    }

    private GameObject BuildTrashIcon(Transform parent)
    {
        var root = NewIconRoot("TrashIcon", parent);
        MakeBox(root.transform, new Vector3(0f, -0.006f, 0f), new Vector3(0.028f, 0.034f, 0.008f)); // body
        MakeBox(root.transform, new Vector3(0f, 0.014f, 0f), new Vector3(0.040f, 0.007f, 0.008f));  // lid
        MakeBox(root.transform, new Vector3(0f, 0.021f, 0f), new Vector3(0.016f, 0.006f, 0.008f));  // handle
        return root;
    }

    private GameObject NewIconRoot(string name, Transform parent)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0f, 0f, IconZ);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        SetLayerRecursively(root, widgetLayer);
        return root;
    }

    // A thin bar of the given length (local X) and square cross-section, rotated about the view axis.
    private void MakeBar(Transform parent, Vector3 localPosition, float zRotationDeg, float length, float thickness)
    {
        var bar = MakeBox(parent, localPosition, new Vector3(length, thickness, thickness));
        bar.transform.localRotation = Quaternion.Euler(0f, 0f, zRotationDeg);
    }

    private GameObject MakeBox(Transform parent, Vector3 localPosition, Vector3 localScale)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "IconPart";
        var collider = box.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider); // icons are decorative only; never ray targets
        }

        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = localScale;
        box.GetComponent<MeshRenderer>().sharedMaterial = _iconMaterial;
        SetLayerRecursively(box, widgetLayer);
        return box;
    }

    private void UpdateWidgetTransform()
    {
        if (_selectedObject == null)
        {
            return;
        }

        Bounds bounds = GetTargetBounds(_selectedObject);
        Transform cameraTransform = Camera.main != null ? Camera.main.transform : null;
        Vector3 cameraRight = cameraTransform != null ? cameraTransform.right : Vector3.right;
        cameraRight.y = 0f;
        if (cameraRight.sqrMagnitude < 0.0001f)
        {
            cameraRight = Vector3.right;
        }

        cameraRight.Normalize();

        Vector3 targetPosition =
            bounds.center +
            Vector3.up * (bounds.extents.y + widgetVerticalOffset) +
            cameraRight * (bounds.extents.x + widgetHorizontalOffset);

        _root.transform.position = targetPosition;

        if (cameraTransform != null)
        {
            Vector3 lookDirection = cameraTransform.position - _root.transform.position;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                _root.transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            }
        }

        if (verboseLogging)
        {
            Vector3 camPos = cameraTransform != null ? cameraTransform.position : Vector3.zero;
            Debug.Log($"[DeleteWidget] pos={targetPosition} boundsCenter={bounds.center} " +
                      $"boundsExt={bounds.extents} rootLossyScale={_root.transform.lossyScale} " +
                      $"cam={camPos} distToCam={(targetPosition - camPos).magnitude:F2}m " +
                      $"rootActive={_root.activeSelf} btnActive={(_deleteButton != null && _deleteButton.activeSelf)}");
        }
    }

    private void UpdateButtonVisuals()
    {
        if (_deleteButton == null || _cancelButton == null)
        {
            return;
        }

        // When armed, the delete button turns into the green "confirm/yes" (check) and the red
        // "no" (cross) button appears beside it.
        _cancelButton.SetActive(_confirmArmed && !_isDeleting);

        _deleteMaterial.color = _isDeleting ? disabledColor : (_confirmArmed ? confirmColor : deleteColor);
        _cancelMaterial.color = _isDeleting ? disabledColor : cancelColor;

        if (_deleteTrashIcon != null)
        {
            _deleteTrashIcon.SetActive(!_confirmArmed && !_isDeleting);
        }

        if (_deleteCheckIcon != null)
        {
            _deleteCheckIcon.SetActive(_confirmArmed && !_isDeleting);
        }
    }

    private void ResetConfirmationState()
    {
        _confirmArmed = false;
        _confirmDeadline = 0f;
        UpdateButtonVisuals();
    }

    private void ApplyVisibility(bool visible)
    {
        if (_root != null)
        {
            _root.SetActive(visible);
        }
    }

    private static Bounds GetTargetBounds(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            return collider.bounds;
        }

        Renderer renderer = target.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            return renderer.bounds;
        }

        return new Bounds(target.transform.position, Vector3.one * 0.2f);
    }

    private static Material CreateButtonMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        return material;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null)
        {
            return;
        }

        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            if (child != null)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
