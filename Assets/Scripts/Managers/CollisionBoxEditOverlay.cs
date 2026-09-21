using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Shows a translucent blue box over a portal/collision box while portal edit mode is active, so
/// every editable box is visible. Selection itself is indicated separately by
/// <see cref="SelectionWireframe"/> (an orange wireframe), so this overlay does not change colour
/// when the box is selected.
/// </summary>
public class CollisionBoxEditOverlay : MonoBehaviour
{
    /// <summary>
    /// The translucent blue used for the edit overlay. Shared so other flows (e.g. the
    /// portal placement preview) can match the exact look of an editable/moving portal.
    /// </summary>
    public static readonly Color EditOverlayColor = new Color(0.15f, 0.8f, 1f, 0.18f);

    [SerializeField] private Color normalColor = EditOverlayColor;

    private GameObject _overlayObject;
    private Material _overlayMaterial;
    private EditModeManager _editModeManager;

    private void Awake()
    {
        EnsureOverlay();
        ApplyVisualState(false);
    }

    private void OnEnable()
    {
        _editModeManager = EditModeManager.Instance;
        if (_editModeManager != null)
        {
            _editModeManager.EditModeChanged += OnEditModeChanged;
            ApplyCurrentState();
        }
    }

    private void OnDisable()
    {
        if (_editModeManager != null)
        {
            _editModeManager.EditModeChanged -= OnEditModeChanged;
        }

        ApplyVisualState(false);
    }

    private void OnDestroy()
    {
        if (_overlayMaterial != null)
        {
            Destroy(_overlayMaterial);
        }
    }

    private void OnEditModeChanged(bool _)
    {
        ApplyCurrentState();
    }

    private void ApplyCurrentState()
    {
        bool isEditMode = _editModeManager != null && _editModeManager.IsEditMode;
        ApplyVisualState(isEditMode);
    }

    private void ApplyVisualState(bool visible)
    {
        EnsureOverlay();
        if (_overlayObject == null || _overlayMaterial == null)
        {
            return;
        }

        _overlayObject.SetActive(visible);
        _overlayMaterial.color = normalColor;
        _overlayObject.transform.localScale = Vector3.one;
    }

    private void EnsureOverlay()
    {
        if (_overlayObject != null && _overlayMaterial != null)
        {
            return;
        }

        if (_overlayObject == null)
        {
            _overlayObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _overlayObject.name = "EditOverlay";
            _overlayObject.transform.SetParent(transform, false);
            _overlayObject.transform.localPosition = Vector3.zero;
            _overlayObject.transform.localRotation = Quaternion.identity;
            _overlayObject.transform.localScale = Vector3.one;

            var collider = _overlayObject.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var overlayFilter = _overlayObject.GetComponent<MeshFilter>();
            var overlayRenderer = _overlayObject.GetComponent<MeshRenderer>();
            if (overlayFilter == null || overlayRenderer == null)
            {
                return;
            }

            _overlayMaterial = CreateOverlayMaterial();
            overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
            overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            overlayRenderer.material = _overlayMaterial;
        }
    }

    /// <summary>
    /// Builds the translucent material used for the edit overlay. Public so other flows can reuse
    /// the exact same render state (e.g. the portal placement preview). Caller owns the instance.
    /// </summary>
    public static Material CreateOverlayMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        var material = new Material(shader);
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.renderQueue = (int)RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        return material;
    }
}
