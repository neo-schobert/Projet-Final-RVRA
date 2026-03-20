using UnityEngine;

/// <summary>
/// Singleton DontDestroyOnLoad — mémorise l'offset physique VR→AR capturé lors
/// du calibrage et fournit les positions/rotations de spawn dans TempleScene.
///
/// SETUP :
///   Ce script est déjà placé sur "CalibrationPersistence" dans Scene.unity.
///   Il survivra automatiquement à la transition de scène.
///
/// FLUX :
///   1. ARProximityCalibration.SaveOffset(arPos, vrPos) lors du calibrage.
///   2. SceneTriggerVR déclenche la transition — aucune sauvegarde supplémentaire
///      nécessaire, l'offset est déjà mémorisé.
///   3. TempleSceneManager lit VRSpawnPosition/Rotation et ARSpawnPosition/Rotation au Start().
///
/// LOGIQUE DE SPAWN dans TempleScene :
///   • VR spawn à (0, 0, 0) — c'est l'ancre de référence, regarde vers l'AR.
///   • AR spawn à (SpawnDistance, 0, 0) en regardant vers (0, 0, 0).
///   SpawnDistance = magnitude XZ de l'offset physique VR−AR capturé au calibrage.
/// </summary>
public class CalibrationPersistence : MonoBehaviour
{
    public static CalibrationPersistence Instance { get; private set; }

    // ── Données de calibration ─────────────────────────────────────────────
    /// <summary>
    /// Offset physique = vrCamera.position - arCamera.position au moment du calibrage.
    /// Représente la distance et direction du VR par rapport à l'AR dans l'espace réel.
    /// </summary>
    public static Vector3 PhysicalOffset { get; private set; }

    /// <summary>True après un appel à SaveOffset réussi.</summary>
    public static bool HasData { get; private set; }

    // ── Données dérivées pour TempleScene ──────────────────────────────────
    /// <summary>
    /// Distance de spawn = magnitude XZ de l'offset physique.
    /// Formule : sqrt(offset.x² + offset.z²)
    /// </summary>
    public static float SpawnDistance =>
        Mathf.Sqrt(PhysicalOffset.x * PhysicalOffset.x +
                   PhysicalOffset.z * PhysicalOffset.z);

    // ── Spawn VR ───────────────────────────────────────────────────────────

    /// <summary>
    /// Position de spawn du XR Origin VR dans TempleScene :
    /// VR spawn à l'ancre (0, 0, 0) — point de référence fixe.
    /// </summary>
    public static Vector3 VRSpawnPosition => Vector3.zero;

    /// <summary>
    /// Rotation de spawn du XR Origin VR dans TempleScene.
    /// Aligne l'espace de tracking VR pour que la direction physique VR→AR
    /// corresponde à la direction virtuelle +X (vers (SpawnDistance, 0, 0)).
    /// </summary>
    public static Quaternion VRSpawnRotation
    {
        get
        {
            // Direction physique du VR vers l'AR (projetée sur XZ)
            // PhysicalOffset = vrPos - arPos → direction VR→AR = -PhysicalOffset
            Vector3 physDirVrToAr = new Vector3(-PhysicalOffset.x, 0f, -PhysicalOffset.z);
            if (physDirVrToAr.sqrMagnitude < 1e-6f) return Quaternion.identity;
            // Angle à appliquer pour faire correspondre physDirVrToAr → +X virtuel
            float angle = Vector3.SignedAngle(physDirVrToAr.normalized, Vector3.right, Vector3.up);
            return Quaternion.AngleAxis(angle, Vector3.up);
        }
    }

    // ── Spawn AR ───────────────────────────────────────────────────────────

    /// <summary>
    /// Position de spawn du XR Origin AR dans TempleScene :
    /// AR spawn sur l'axe X positif à la distance SpawnDistance, en face du VR.
    /// </summary>
    public static Vector3 ARSpawnPosition => new Vector3(SpawnDistance, 0f, 0f);

    /// <summary>
    /// Rotation de spawn du XR Origin AR dans TempleScene.
    /// Aligne l'espace de tracking AR pour que la direction physique AR→VR
    /// corresponde à la direction virtuelle -X (vers (0, 0, 0)).
    /// </summary>
    public static Quaternion ARSpawnRotation
    {
        get
        {
            // Direction physique de l'AR vers le VR (projetée sur XZ)
            // PhysicalOffset = vrPos - arPos → direction AR→VR = +PhysicalOffset
            Vector3 physDirArToVr = new Vector3(PhysicalOffset.x, 0f, PhysicalOffset.z);
            if (physDirArToVr.sqrMagnitude < 1e-6f) return Quaternion.identity;
            // Angle à appliquer pour faire correspondre physDirArToVr → -X virtuel
            float angle = Vector3.SignedAngle(physDirArToVr.normalized, -Vector3.right, Vector3.up);
            return Quaternion.AngleAxis(angle, Vector3.up);
        }
    }

    // ───────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[CalibrationPersistence] Singleton prêt — survivra à la transition de scène.");
    }

    // ── API publique ────────────────────────────────────────────────────────

    /// <summary>
    /// Sauvegarde l'offset physique entre les deux joueurs au moment du calibrage.
    /// Appelé par ARProximityCalibration juste avant l'alignement du XR Origin AR.
    /// </summary>
    /// <param name="arPhysicalPosition">Camera.main.position côté AR au moment du calibrage.</param>
    /// <param name="vrPhysicalPosition">PlayerSetup.transform.position côté VR au moment du calibrage.</param>
    public static void SaveOffset(Vector3 arPhysicalPosition, Vector3 vrPhysicalPosition)
    {
        PhysicalOffset = vrPhysicalPosition - arPhysicalPosition;
        HasData        = true;

        Debug.Log($"[CalibrationPersistence] ✓ Offset sauvegardé → " +
                  $"offset={PhysicalOffset:F3} | " +
                  $"SpawnDistance={SpawnDistance:F3}m | " +
                  $"VR spawn={VRSpawnPosition:F3} rot={VRSpawnRotation.eulerAngles:F1} | " +
                  $"AR spawn={ARSpawnPosition:F3}");
    }

    /// <summary>
    /// Met à jour l'offset physique en silence — appelé chaque frame par ARProximityCalibration
    /// pour tracker la distance réelle VR↔AR pendant toute la session (vue normale ET aérienne).
    /// Contrairement à SaveOffset, n'émet pas de log pour éviter le flood.
    /// </summary>
    public static void UpdateOffset(Vector3 arPhysicalPosition, Vector3 vrPhysicalPosition)
    {
        PhysicalOffset = vrPhysicalPosition - arPhysicalPosition;
        // HasData reste inchangé — UpdateOffset n'est appelé qu'après SaveOffset initial.
    }
}
