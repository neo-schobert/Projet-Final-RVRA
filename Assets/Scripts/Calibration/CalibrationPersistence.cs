using UnityEngine;

/// <summary>
/// Singleton DontDestroyOnLoad — mémorise la calibration et fournit les positions/rotations
/// de spawn dans TempleScene.
///
/// DESIGN :
///   arCam.transform.localPosition = position physique ARCore en mètres réels,
///   relative à Camera Offset (parent direct). Indépendante du XR Origin.
///   → Même en vue aérienne (XR Origin repositionné), localPosition reste correct.
///
///   Formule finale :
///   ARSpawnPosition = VRSpawnRotation * xrOriginRot * (localPos_now - localPos_calib)
///     - localPos_now   : position ARCore actuelle (mètres physiques)
///     - localPos_calib : position ARCore au moment de la calibration
///     - xrOriginRot    : rotation XR Origin post-alignement (local → monde Scene.unity)
///     - VRSpawnRotation: monde Scene.unity → espace TempleScene (VR at 0,0,0 looking +X)
/// </summary>
public class CalibrationPersistence : MonoBehaviour
{
    public static CalibrationPersistence Instance { get; private set; }
    public static bool HasData { get; private set; }

    // ── Données de calibration ──────────────────────────────────────────────
    private static float      _vrYawAtCalib         = 0f;
    private static Quaternion _xrOriginRotAtCalib   = Quaternion.identity;
    private static Vector3    _camLocalPosAtCalib   = Vector3.zero;

    // ── Spawn actuel ────────────────────────────────────────────────────────
    private static Vector3    _arPhysicalDisplacement = Vector3.zero;

    // ── API spawn VR ────────────────────────────────────────────────────────
    public static Vector3    VRSpawnPosition => Vector3.zero;

    public static Quaternion VRSpawnRotation
    {
        get
        {
            if (!HasData) return Quaternion.identity;
            // Direction physique VR→AR au moment de la calibration = vrYaw forward.
            // On la mappe vers +X virtuel dans TempleScene.
            Vector3 vrForward = Quaternion.Euler(0f, _vrYawAtCalib, 0f) * Vector3.forward;
            float   angle     = Vector3.SignedAngle(vrForward, Vector3.right, Vector3.up);
            return Quaternion.AngleAxis(angle, Vector3.up);
        }
    }

    // ── API spawn AR ────────────────────────────────────────────────────────
    public static Vector3    ARSpawnPosition  => _arPhysicalDisplacement;

    /// <summary>
    /// Rotation du XR Origin AR dans TempleScene.
    /// = VRSpawnRotation * _xrOriginRotAtCalib
    /// → préserve le mapping physique local → TempleScene.
    /// </summary>
    public static Quaternion ARSpawnRotation  => VRSpawnRotation * _xrOriginRotAtCalib;

    public static float SpawnDistance =>
        new Vector2(_arPhysicalDisplacement.x, _arPhysicalDisplacement.z).magnitude;

    // ── Lifecycle ───────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── API publique ────────────────────────────────────────────────────────

    /// <summary>
    /// Appelé une fois juste après AlignerARSurVR.
    /// </summary>
    /// <param name="vrYaw">vrTarget.eulerAngles.y au moment de la calibration.</param>
    /// <param name="xrOriginRot">_arXROrigin.rotation APRÈS alignement.</param>
    /// <param name="camLocalPosAtCalib">arCam.transform.localPosition APRÈS alignement.</param>
    public static void SaveCalibration(float vrYaw, Quaternion xrOriginRot, Vector3 camLocalPosAtCalib)
    {
        _vrYawAtCalib       = vrYaw;
        _xrOriginRotAtCalib = xrOriginRot;
        _camLocalPosAtCalib = camLocalPosAtCalib;
        _arPhysicalDisplacement = Vector3.zero;
        HasData             = true;

        Debug.Log($"[CalibrationPersistence] ✓ Calibration — " +
                  $"vrYaw={vrYaw:F1}° | xrOriginRot={xrOriginRot.eulerAngles:F1}° | " +
                  $"camLocalAtCalib={camLocalPosAtCalib:F3}");
    }

    /// <summary>
    /// Mis à jour chaque frame par ARProximityCalibration.
    /// camLocalPosCurrent = arCam.transform.localPosition (position ARCore physique actuelle).
    /// </summary>
    public static void UpdateARDisplacement(Vector3 camLocalPosCurrent)
    {
        if (!HasData) return;

        // Déplacement physique en mètres réels depuis la calibration (dans l'espace local ARCore)
        Vector3 localDelta = camLocalPosCurrent - _camLocalPosAtCalib;
        localDelta.y = 0f;

        // localDelta est dans l'espace local du XR Origin (espace ARCore).
        // _xrOriginRotAtCalib le convertit en espace monde Scene.unity.
        // VRSpawnRotation le convertit en espace TempleScene (VR à l'origine, looking +X).
        _arPhysicalDisplacement = VRSpawnRotation * (_xrOriginRotAtCalib * localDelta);

        Debug.Log($"[CalibPersist] localDelta={localDelta:F3} → ARSpawnPos={_arPhysicalDisplacement:F3}");
    }
}
