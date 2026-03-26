using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Outils éditeur pour les matériaux d'explosion Mirza Beig.
///
/// FIX 1 — "Fix Explosion Materials (URP)" :
///   Convertit les shaders Built-in → URP/Particles/Unlit (matériaux roses).
///
/// FIX 2 — "Fix Explosion Distortion (VR invisible)" :
///   Désactive _DISTORTION_ON sur tous les matériaux d'explosion.
///
///   CAUSE : _DISTORTION_ON active une "distortion pass" qui sample
///   _CameraOpaqueTexture pour déformer l'arrière-plan. En XR VR
///   (single-pass instanced), cette texture n'est pas correctement disponible
///   pour les deux yeux → les particules sont INVISIBLES côté VR.
///   PC_RPAsset a RequireOpaqueTexture=1 (VR) mais le grab ne fonctionne
///   pas en stéréo → invisible. Mobile_RPAsset a RequireOpaqueTexture=0
///   (AR) → fallback → visible normalement.
///
///   SOLUTION : désactiver _DISTORTION_ON. L'explosion reste visible sans
///   l'effet de distorsion de chaleur.
/// </summary>
public static class FixExplosionMaterials
{
    // ── Fix 2 : Désactiver la distortion (explosions invisibles en VR) ────────

    [MenuItem("Tools/Fix Explosion Distortion (VR invisible)")]
    public static void DisableDistortion()
    {
        string[] guids = AssetDatabase.FindAssets(
            "t:Material", new[] { "Assets/Mirza Beig" });

        int fixedCount   = 0;
        int skippedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            if (mat.IsKeywordEnabled("_DISTORTION_ON"))
            {
                mat.DisableKeyword("_DISTORTION_ON");
                EditorUtility.SetDirty(mat);
                Debug.Log($"[FixExplosionMaterials] ✓ Distortion OFF : {Path.GetFileName(path)}");
                fixedCount++;
            }
            else
            {
                skippedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Fix Explosion Distortion — Terminé",
            $"✓ {fixedCount} matériau(x) corrigé(s) (_DISTORTION_ON désactivé)\n" +
            $"⏭ {skippedCount} matériau(x) ignoré(s) (distortion déjà désactivée)\n\n" +
            "Les explosions seront maintenant visibles côté VR.\n" +
            "L'effet de distorsion de chaleur est supprimé (trade-off VR).",
            "OK");
    }

    // ── Fix 1 : Conversion shader Built-in → URP (matériaux roses) ───────────
    // Dossier à scanner (relatif à Assets/)
    private const string TargetFolder = "Assets/Mirza Beig/Cinematic Explosions FREE/Materials";

    // Shader URP particules sans éclairage — idéal pour les VFX / explosions.
    // Utilise "Lit" si tu veux des réactions à la lumière de la scène.
    private const string URPShaderName = "Universal Render Pipeline/Particles/Unlit";

    [MenuItem("Tools/Fix Explosion Materials (URP)")]
    public static void Fix()
    {
        Shader urpShader = Shader.Find(URPShaderName);
        if (urpShader == null)
        {
            EditorUtility.DisplayDialog(
                "Fix Explosion Materials",
                $"Shader introuvable : '{URPShaderName}'\n\n" +
                "Vérifie que le package URP est bien installé dans le projet.",
                "OK");
            return;
        }

        // Trouve tous les .mat dans le dossier cible (récursif)
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { TargetFolder });

        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Fix Explosion Materials",
                $"Aucun matériau trouvé dans :\n{TargetFolder}",
                "OK");
            return;
        }

        int fixed_count = 0;
        int skipped     = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null) continue;

            // Déjà un shader URP → on ne touche pas
            if (mat.shader != null && mat.shader.name.StartsWith("Universal Render Pipeline"))
            {
                skipped++;
                continue;
            }

            string oldShader = mat.shader != null ? mat.shader.name : "null";
            mat.shader = urpShader;
            EditorUtility.SetDirty(mat);

            Debug.Log($"[FixExplosionMaterials] ✓ {Path.GetFileName(path)} : '{oldShader}' → '{URPShaderName}'");
            fixed_count++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Fix Explosion Materials — Terminé",
            $"✓ {fixed_count} matériau(x) converti(s) vers URP Particles/Unlit\n" +
            $"⏭ {skipped} déjà en URP (ignorés)\n\n" +
            "Si des textures ont disparu après la conversion, réassigne-les manuellement " +
            "dans l'Inspector de chaque matériau (champ 'Base Map').",
            "OK");
    }
}
