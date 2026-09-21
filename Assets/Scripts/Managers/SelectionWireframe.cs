using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws a wireframe bounding box around an object while it is the EditModeManager's selected
/// object, so the user can see what is currently picked. Shared by portals and MAT objects for a
/// consistent look (no colour-change overlay). The box is sized from the object's BoxCollider (the
/// same volume used for ray selection) and falls back to the combined renderer bounds.
/// </summary>
public class SelectionWireframe : MonoBehaviour
{
    [SerializeField] private Color color = new Color(1f, 0.55f, 0.1f, 1f);

    private EditModeManager _editModeManager;
    private GameObject _wireObject;
    private Material _wireMaterial;
    private BoxCollider _boxCollider;

    private void Awake()
    {
        _boxCollider = GetComponentInChildren<BoxCollider>();
        EnsureWireframe();
        SetVisible(false);
    }

    private void OnEnable()
    {
        _editModeManager = EditModeManager.Instance;
        if (_editModeManager != null)
        {
            _editModeManager.SelectedObjectChanged += OnSelectedChanged;
            _editModeManager.EditModeChanged += OnModeChanged;
            _editModeManager.MatEditModeChanged += OnModeChanged;
            ApplyState();
        }
    }

    private void OnDisable()
    {
        if (_editModeManager != null)
        {
            _editModeManager.SelectedObjectChanged -= OnSelectedChanged;
            _editModeManager.EditModeChanged -= OnModeChanged;
            _editModeManager.MatEditModeChanged -= OnModeChanged;
        }

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (_wireMaterial != null)
        {
            Destroy(_wireMaterial);
        }
    }

    private void OnSelectedChanged(GameObject _)
    {
        ApplyState();
    }

    private void OnModeChanged(bool _)
    {
        ApplyState();
    }

    private void ApplyState()
    {
        bool selected = _editModeManager != null && _editModeManager.SelectedObject == gameObject;
        if (selected)
        {
            UpdateBoxToBounds();
        }

        SetVisible(selected);
    }

    private void UpdateBoxToBounds()
    {
        if (_wireObject == null)
        {
            return;
        }

        if (_boxCollider == null)
        {
            _boxCollider = GetComponentInChildren<BoxCollider>();
        }

        if (_boxCollider != null)
        {
            // Match the collider volume exactly, in the collider's local space so it follows any
            // rotation of the object.
            _wireObject.transform.SetParent(_boxCollider.transform, false);
            _wireObject.transform.localPosition = _boxCollider.center;
            _wireObject.transform.localRotation = Quaternion.identity;
            _wireObject.transform.localScale = _boxCollider.size;
            return;
        }

        // Fallback: world-axis-aligned box from the renderer bounds.
        var childRenderer = GetComponentInChildren<Renderer>();
        if (childRenderer == null)
        {
            return;
        }

        Bounds bounds = childRenderer.bounds;
        _wireObject.transform.SetParent(null, true);
        _wireObject.transform.position = bounds.center;
        _wireObject.transform.rotation = Quaternion.identity;
        _wireObject.transform.localScale = bounds.size;
    }

    private void SetVisible(bool visible)
    {
        if (_wireObject != null)
        {
            _wireObject.SetActive(visible);
        }
    }

    private void EnsureWireframe()
    {
        if (_wireObject != null)
        {
            return;
        }

        _wireObject = new GameObject("MatSelectionWireframe");
        _wireObject.transform.SetParent(transform, false);

        var meshFilter = _wireObject.AddComponent<MeshFilter>();
        var meshRenderer = _wireObject.AddComponent<MeshRenderer>();
        meshFilter.sharedMesh = BuildWireBoxMesh();
        _wireMaterial = CreateWireMaterial();
        meshRenderer.sharedMaterial = _wireMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // Unit cube (edge length 1, centred at origin) as a line-topology mesh: 8 corners, 12 edges.
    private static Mesh BuildWireBoxMesh()
    {
        var vertices = new Vector3[8];
        for (int idx = 0; idx < 8; idx++)
        {
            float x = (idx & 1) != 0 ? 0.5f : -0.5f;
            float y = (idx & 2) != 0 ? 0.5f : -0.5f;
            float z = (idx & 4) != 0 ? 0.5f : -0.5f;
            vertices[idx] = new Vector3(x, y, z);
        }

        int[] edges =
        {
            0, 1, 2, 3, 4, 5, 6, 7, // edges along X (corners differing in bit 0)
            0, 2, 1, 3, 4, 6, 5, 7, // edges along Y (bit 1)
            0, 4, 1, 5, 2, 6, 3, 7  // edges along Z (bit 2)
        };

        var mesh = new Mesh { name = "MatSelectionWireBox" };
        mesh.vertices = vertices;
        mesh.SetIndices(edges, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private Material CreateWireMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        var material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        // Draw on top so the selection box stays visible through the MAT geometry.
        material.renderQueue = (int)RenderQueue.Overlay;
        return material;
    }
}
