#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Editor utility to collapse a deep mesh hierarchy (e.g. the 177-renderer tile prefab)
// into a single MeshRenderer, cutting the draw-call count from ~one-per-part down to
// one-per-material. Big win on Quest, where draw calls are the expensive CPU cost.
//
// Usage:
//   1. Drag the prefab into the scene (or open it) and select its ROOT GameObject.
//   2. Tools -> Mozart -> Combine Selected Mesh Hierarchy.
//   3. A combined mesh asset is written to Assets/Models/Combined/ and a new
//      "<name>_combined" object is created next to the original. Verify it looks right,
//      then save it as your new prefab and delete the heavy original.
public static class MeshCombiner
{
    private const string OutputDir = "Assets/Models/Combined";

    [MenuItem("Tools/Mozart/Combine Selected Mesh Hierarchy")]
    public static void CombineSelected()
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("Combine Mesh",
                "Select the root GameObject of the mesh hierarchy in the scene first.", "OK");
            return;
        }

        var filters = root.GetComponentsInChildren<MeshFilter>(false)
                          .Where(f => f.sharedMesh != null)
                          .ToArray();
        if (filters.Length == 0)
        {
            EditorUtility.DisplayDialog("Combine Mesh",
                "No active MeshFilters with a mesh were found under the selection.", "OK");
            return;
        }

        // Bake every part into the root's LOCAL space so the combined mesh aligns when
        // the new object is placed at the root's transform.
        Matrix4x4 worldToRoot = root.transform.worldToLocalMatrix;

        // Group by material: one combined submesh per distinct material = one draw call each.
        var byMaterial = new Dictionary<Material, List<CombineInstance>>();
        var materialOrder = new List<Material>();

        foreach (var f in filters)
        {
            var mesh = f.sharedMesh;
            var renderer = f.GetComponent<MeshRenderer>();
            var mats = renderer != null ? renderer.sharedMaterials : null;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                Material mat = (mats != null && sub < mats.Length) ? mats[sub]
                             : (mats != null && mats.Length > 0) ? mats[0]
                             : null;

                if (!byMaterial.TryGetValue(mat, out var list))
                {
                    list = new List<CombineInstance>();
                    byMaterial[mat] = list;
                    materialOrder.Add(mat);
                }

                list.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = sub,
                    transform = worldToRoot * f.transform.localToWorldMatrix
                });
            }
        }

        // Pass 1: merge all parts of each material into a single-submesh mesh.
        var perMaterialMeshes = new List<Mesh>();
        var finalCombine = new List<CombineInstance>();
        foreach (var mat in materialOrder)
        {
            var part = new Mesh { indexFormat = IndexFormat.UInt32 };
            part.CombineMeshes(byMaterial[mat].ToArray(), mergeSubMeshes: true, useMatrices: true);
            perMaterialMeshes.Add(part);
            finalCombine.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
        }

        // Pass 2: combine the per-material meshes into ONE mesh, keeping them as separate
        // submeshes (so materialOrder maps 1:1 to submeshes).
        var combined = new Mesh
        {
            name = root.name + "_combined",
            indexFormat = IndexFormat.UInt32
        };
        combined.CombineMeshes(finalCombine.ToArray(), mergeSubMeshes: false, useMatrices: false);
        combined.RecalculateBounds();
        // Note: CombineMeshes preserves the source vertex normals; don't RecalculateNormals()
        // here or hard edges/custom shading from the model would be destroyed.

        // Clean up the temporary per-material meshes.
        foreach (var m in perMaterialMeshes) Object.DestroyImmediate(m);

        // Save the combined mesh as an asset.
        Directory.CreateDirectory(OutputDir);
        string meshPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputDir}/{root.name}_combined.asset");
        AssetDatabase.CreateAsset(combined, meshPath);
        AssetDatabase.SaveAssets();

        // Create the single-renderer result next to the original.
        var go = new GameObject(root.name + "_combined");
        go.transform.SetParent(root.transform.parent, false);
        go.transform.localPosition = root.transform.localPosition;
        go.transform.localRotation = root.transform.localRotation;
        go.transform.localScale = root.transform.localScale;

        go.AddComponent<MeshFilter>().sharedMesh = combined;
        go.AddComponent<MeshRenderer>().sharedMaterials = materialOrder.ToArray();

        Undo.RegisterCreatedObjectUndo(go, "Combine Mesh Hierarchy");
        Selection.activeGameObject = go;
        root.SetActive(false); // hide the original so you can compare; delete it once happy.

        int before = filters.Length;
        int after = materialOrder.Count;
        Debug.Log($"[MeshCombiner] Combined {before} mesh parts into 1 object with {after} " +
                  $"submesh(es)/material(s), {combined.vertexCount} verts. " +
                  $"Draw calls ~{before} -> {after}. Saved: {meshPath}");
        EditorUtility.DisplayDialog("Combine Mesh",
            $"Combined {before} parts into 1 object.\n" +
            $"Submeshes/materials: {after}  (draw calls ~{before} -> {after})\n" +
            $"Verts: {combined.vertexCount:N0}\n\n" +
            $"Mesh asset:\n{meshPath}\n\n" +
            "The original was hidden (not deleted). Verify the combined object, save it as " +
            "your new prefab, then delete the original.", "OK");
    }

    // Only enabled when a GameObject is selected.
    [MenuItem("Tools/Mozart/Combine Selected Mesh Hierarchy", true)]
    private static bool CombineSelectedValidate() => Selection.activeGameObject != null;
}
#endif
