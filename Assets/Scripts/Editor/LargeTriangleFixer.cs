using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Outil éditeur pour corriger les warnings PhysX :
///   "Detected one or more triangles where the distance between any 2 vertices
///    is greater than 500 units."
///
/// CAUSE :
///   Des MeshColliders utilisent des maillages avec de TRÈS grands triangles
///   (géométrie de temple/paysage importée à grande échelle).
///   PhysX recommande la tessellation, mais en pratique :
///     - Si le mesh n'a pas besoin de collisions précises → désactiver le MeshCollider.
///     - Si convex = acceptable → passer en Convex (PhysX triangule automatiquement).
///
/// UTILISATION :
///   Menu Unity → Tools → Physique → Rapport MeshColliders grandes triangles
///   Menu Unity → Tools → Physique → Forcer Convex sur MeshColliders problématiques
///
/// NOTE : "Forcer Convex" convient aux murs/sol simples.
///        Pour une géométrie concave complexe, préfère des colliders primitifs
///        (Box/Capsule/Sphere) créés manuellement.
/// </summary>
public static class LargeTriangleFixer
{
    // Seuil : si un triangle du mesh a un côté > cette valeur, on le signale.
    private const float THRESHOLD = 450f;

    [MenuItem("Tools/Physique/Rapport MeshColliders grandes triangles")]
    public static void ReportLargeMeshColliders()
    {
        var results = FindProblematicColliders();

        if (results.Count == 0)
        {
            Debug.Log("[LargeTriangleFixer] ✓ Aucun MeshCollider avec grandes triangles trouvé.");
            return;
        }

        Debug.LogWarning($"[LargeTriangleFixer] {results.Count} MeshCollider(s) potentiellement problématique(s) :");
        foreach (var (go, mc, meshName) in results)
            Debug.LogWarning($"  → '{GetPath(go)}' | mesh: '{meshName}' | convex: {mc.convex}", go);

        Debug.Log("[LargeTriangleFixer] Utilise 'Forcer Convex' pour corriger automatiquement, " +
                  "ou remplace par des colliders primitifs manuellement.");
    }

    // ⚠️ DÉCONSEILLÉ pour les géométries concaves (villa, bâtiments avec pièces, portes).
    // Passer convex=true sur une géométrie concave crée une enveloppe convexe qui
    // remplit tout l'intérieur → murs invisibles → impossibilité de se déplacer.
    // Préfère "Rapport" pour identifier les meshes, puis remplace manuellement
    // par des colliders primitifs (Box/Capsule) sur les parties problématiques.
    [MenuItem("Tools/Physique/Forcer Convex sur MeshColliders problématiques (DANGEREUX - concave!)")]
    public static void ForceConvexOnProblematic()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "⚠️ Attention — Géométrie concave",
            "Forcer convex=true sur un bâtiment avec des pièces/portes (ex: villa) " +
            "crée une enveloppe convexe qui REMPLIT l'intérieur → murs invisibles partout.\n\n" +
            "Utilise cette option UNIQUEMENT sur des objets simples (rochers, sol plat).\n\n" +
            "Continuer quand même ?",
            "Oui, je comprends",
            "Annuler");

        if (!confirmed) return;

        var results = FindProblematicColliders();

        if (results.Count == 0)
        {
            Debug.Log("[LargeTriangleFixer] ✓ Aucun MeshCollider problématique trouvé.");
            return;
        }

        int fixed_ = 0;
        foreach (var (go, mc, meshName) in results)
        {
            if (!mc.convex)
            {
                Undo.RecordObject(mc, "Force Convex MeshCollider");
                mc.convex = true;
                EditorUtility.SetDirty(go);
                fixed_++;
                Debug.Log($"[LargeTriangleFixer] Convex activé sur '{GetPath(go)}' (mesh: {meshName})", go);
            }
        }

        if (fixed_ > 0)
        {
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[LargeTriangleFixer] ✓ {fixed_} MeshCollider(s) passés en Convex. Scènes sauvegardées.");
        }
        else
        {
            Debug.Log("[LargeTriangleFixer] Tous les MeshColliders problématiques sont déjà Convex.");
        }
    }

    /// <summary>
    /// Annule le "Forcer Convex" : repasse convex=false sur tous les MeshColliders
    /// avec de grands triangles qui avaient été malencontreusement passés en convex.
    /// Utilise ça si la villa/le bâtiment est devenu impénétrable après "Forcer Convex".
    /// </summary>
    [MenuItem("Tools/Physique/RESTAURER — Annuler Forcer Convex sur grandes triangles")]
    public static void RevertConvexOnProblematic()
    {
        var results = FindProblematicColliders();

        if (results.Count == 0)
        {
            Debug.Log("[LargeTriangleFixer] ✓ Aucun MeshCollider avec grandes triangles trouvé.");
            return;
        }

        int reverted = 0;
        foreach (var (go, mc, meshName) in results)
        {
            if (mc.convex)
            {
                Undo.RecordObject(mc, "Revert Convex MeshCollider");
                mc.convex = false;
                EditorUtility.SetDirty(go);
                reverted++;
                Debug.Log($"[LargeTriangleFixer] convex=false restauré sur '{GetPath(go)}' (mesh: {meshName})", go);
            }
        }

        if (reverted > 0)
        {
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[LargeTriangleFixer] ✓ {reverted} MeshCollider(s) repassés en non-convex. " +
                      "Scènes sauvegardées. Les warnings PhysX réapparaîtront mais la collision concave est restaurée.");
        }
        else
        {
            Debug.Log("[LargeTriangleFixer] Aucun MeshCollider de grandes triangles n'était convex — rien à restaurer.");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static List<(GameObject go, MeshCollider mc, string meshName)> FindProblematicColliders()
    {
        var results = new List<(GameObject, MeshCollider, string)>();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
                FindRecursive(root, results);
        }

        return results;
    }

    private static void FindRecursive(
        GameObject go,
        List<(GameObject, MeshCollider, string)> results)
    {
        var mc = go.GetComponent<MeshCollider>();
        if (mc != null && mc.sharedMesh != null)
        {
            if (HasLargeTriangle(mc.sharedMesh))
                results.Add((go, mc, mc.sharedMesh.name));
        }

        foreach (Transform child in go.transform)
            FindRecursive(child.gameObject, results);
    }

    private static bool HasLargeTriangle(Mesh mesh)
    {
        Vector3[] verts = mesh.vertices;
        int[]     tris  = mesh.triangles;

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];

            if (Vector3.Distance(a, b) > THRESHOLD ||
                Vector3.Distance(b, c) > THRESHOLD ||
                Vector3.Distance(a, c) > THRESHOLD)
                return true;
        }
        return false;
    }

    private static string GetPath(GameObject go)
    {
        var parts = new List<string>();
        Transform t = go.transform;
        while (t != null)
        {
            parts.Insert(0, t.name);
            t = t.parent;
        }
        return string.Join("/", parts);
    }
}
