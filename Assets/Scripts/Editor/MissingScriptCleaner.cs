using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Outil éditeur pour trouver et supprimer les composants "Missing Script"
/// dans toutes les scènes du projet et les prefabs.
///
/// UTILISATION :
///   Menu Unity → Tools → Nettoyer les scripts manquants
///
/// CAUSE FRÉQUENTE :
///   Un script a été supprimé ou renommé sans mettre à jour les références
///   dans la scène → Unity conserve le composant avec une référence null.
/// </summary>
public static class MissingScriptCleaner
{
    [MenuItem("Tools/Nettoyer les scripts manquants (scènes ouvertes)")]
    public static void CleanMissingScriptsInOpenScenes()
    {
        int totalCleaned = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            int cleaned = CleanScene(scene);
            totalCleaned += cleaned;
            Debug.Log($"[MissingScriptCleaner] Scène '{scene.name}' : {cleaned} script(s) manquant(s) supprimé(s).");
        }

        if (totalCleaned > 0)
        {
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[MissingScriptCleaner] ✓ Total : {totalCleaned} script(s) manquant(s) supprimé(s). Scènes sauvegardées.");
        }
        else
        {
            Debug.Log("[MissingScriptCleaner] ✓ Aucun script manquant trouvé dans les scènes ouvertes.");
        }
    }

    [MenuItem("Tools/Trouver les scripts manquants (rapport seulement)")]
    public static void FindMissingScriptsReport()
    {
        int total = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
                total += ReportMissingRecursive(root, scene.name);
        }

        Debug.Log(total > 0
            ? $"[MissingScriptCleaner] {total} script(s) manquant(s) trouvé(s) — " +
              "utilise 'Nettoyer les scripts manquants' pour les supprimer."
            : "[MissingScriptCleaner] ✓ Aucun script manquant trouvé.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static int CleanScene(Scene scene)
    {
        int count = 0;
        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
            count += CleanRecursive(root);
        return count;
    }

    private static int CleanRecursive(GameObject go)
    {
        int count = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);

        if (count > 0)
            Debug.Log($"[MissingScriptCleaner]   → Supprimé {count} script(s) manquant(s) sur '{GetPath(go)}'");

        foreach (Transform child in go.transform)
            count += CleanRecursive(child.gameObject);

        return count;
    }

    private static int ReportMissingRecursive(GameObject go, string sceneName)
    {
        int count = 0;
        var components = go.GetComponents<Component>();
        foreach (var c in components)
        {
            if (c == null)
            {
                count++;
                Debug.LogWarning($"[MissingScriptCleaner] Script manquant sur : [{sceneName}] {GetPath(go)}",
                                 go);
                break; // un seul log par GO pour éviter le flood
            }
        }

        foreach (Transform child in go.transform)
            count += ReportMissingRecursive(child.gameObject, sceneName);

        return count;
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
