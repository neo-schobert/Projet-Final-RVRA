using UnityEngine;

/// <summary>
/// Singleton DontDestroyOnLoad — mémorise les 4 valeurs de calibration
/// et les expose pour TempleSceneManager.
///
/// VALEURS STOCKÉES :
///   • VrPhysicalYaw      — yaw physique du casque VR (hors joystick), en degrés.
///   • VrPosition         — position monde VR au moment de la calibration.
///   • ArYaw              — yaw monde de la caméra AR (mis à jour en continu).
///   • ArPhysicalPosition — position monde de la caméra AR (mise à jour en continu).
///
/// MISE À JOUR APRÈS CALIBRATION :
///   UpdateAR()          — appelé chaque frame par ARProximityCalibration (tracking continu AR).
///   UpdateVrPhysicalYaw() — à appeler côté VR si le yaw physique évolue après calibration.
/// </summary>
public class CalibrationPersistence : MonoBehaviour
{
    public static CalibrationPersistence Instance { get; private set; }
    public static bool HasData { get; private set; }

    // ── 4 valeurs fondamentales ────────────────────────────────────────────
    private static float   _vrPhysicalYaw      = 0f;
    private static Vector3 _vrPosition         = Vector3.zero;
    private static float   _arYaw              = 0f;
    private static Vector3 _arPhysicalPosition = Vector3.zero;

    // ── Propriétés exposées ────────────────────────────────────────────────
    /// <summary>Yaw physique du joueur VR (hors joystick), en degrés.</summary>
    public static float   VrPhysicalYaw      => _vrPhysicalYaw;

    /// <summary>Position monde du joueur VR au moment de la calibration.</summary>
    public static Vector3 VrPosition         => _vrPosition;

    /// <summary>Yaw monde de la caméra AR (mis à jour en continu).</summary>
    public static float   ArYaw              => _arYaw;

    /// <summary>Position monde de la caméra AR (mise à jour en continu).</summary>
    public static Vector3 ArPhysicalPosition => _arPhysicalPosition;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── API publique ───────────────────────────────────────────────────────

    /// <summary>
    /// Appelé une fois juste après la calibration.
    /// Enregistre les 4 valeurs fondamentales.
    /// </summary>
    /// <param name="vrYaw">Yaw physique VR (vrTarget.eulerAngles.y).</param>
    /// <param name="vrPosition">Position monde VR (vrTarget.position).</param>
    /// <param name="arYaw">Yaw monde de la caméra AR (arCam.transform.eulerAngles.y).</param>
    /// <param name="arPosition">Position monde de la caméra AR (arCam.transform.position).</param>
    public static void SaveCalibration(float vrYaw, Vector3 vrPosition,
                                       float arYaw, Vector3 arPosition)
    {
        _vrPhysicalYaw      = vrYaw;
        _vrPosition         = vrPosition;
        _arYaw              = arYaw;
        _arPhysicalPosition = arPosition;
        HasData             = true;

        Debug.Log($"[CalibrationPersistence] ✓ SaveCalibration — " +
                  $"vrYaw={vrYaw:F1}°  vrPos={vrPosition:F3}  " +
                  $"arYaw={arYaw:F1}°  arPos={arPosition:F3}");
    }

    /// <summary>
    /// Mis à jour chaque frame par ARProximityCalibration (tracking AR continu).
    /// </summary>
    /// <param name="arYaw">Yaw monde actuel de la caméra AR.</param>
    /// <param name="arPhysicalPosition">Position monde actuelle de la caméra AR.</param>
    public static void UpdateAR(float arYaw, Vector3 arPhysicalPosition)
    {
        if (!HasData) return;
        _arYaw              = arYaw;
        _arPhysicalPosition = arPhysicalPosition;
    }

    /// <summary>
    /// À appeler côté VR si le yaw physique change après la calibration initiale.
    /// </summary>
    /// <param name="vrPhysicalYaw">Nouveau yaw physique VR (hors joystick).</param>
    public static void UpdateVrPhysicalYaw(float vrPhysicalYaw)
    {
        if (!HasData) return;
        _vrPhysicalYaw = vrPhysicalYaw;
    }
}
