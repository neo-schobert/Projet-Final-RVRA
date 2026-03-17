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
///   • AR spawn à (0, 0, 0)  — c'est l'ancre de référence.
///   • VR spawn à (SpawnDistance, 0, 0) en regardant vers (0, 0, 0).
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
    /// VR spawn sur l'axe X positif à la distance SpawnDistance.
    /// </summary>
    public static Vector3 VRSpawnPosition => new Vector3(SpawnDistance, 0f, 0f);

    /// <summary>
    /// Rotation de spawn du XR Origin VR dans TempleScene :
    /// VR regarde vers Vector3.zero (vers le joueur AR).
    /// </summary>
    public static Quaternion VRSpawnRotation
    {
        get
        {
            // Direction de (SpawnDistance, 0, 0) vers (0, 0, 0)
            Vector3 forward = (Vector3.zero - VRSpawnPosition).normalized;
            // forward = (-1, 0, 0) quand SpawnDistance > 0
            // Cas dégénéré (offset nul) : regarder en avant par défaut
            if (forward == Vector3.zero) forward = Vector3.forward;
            return Quaternion.LookRotation(forward, Vector3.up);
        }
    }

    // ── Spawn AR ───────────────────────────────────────────────────────────

    /// <summary>
    /// Position de spawn du XR Origin AR dans TempleScene : l'AR est l'ancre à (0, 0, 0).
    /// </summary>
    public static Vector3 ARSpawnPosition => Vector3.zero;

    /// <summary>
    /// Rotation de spawn du XR Origin AR dans TempleScene : rotation identité.
    /// </summary>
    public static Quaternion ARSpawnRotation => Quaternion.identity;

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
}
