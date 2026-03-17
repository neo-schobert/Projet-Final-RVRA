using UnityEngine;

/// <summary>
/// Singleton DontDestroyOnLoad — mémorise les offsets de calibration
/// (position/rotation des XR Origins) pour les réappliquer dans TempleScene.
///
/// SETUP :
///   Placer ce script sur un GameObject vide dans Scene.unity (ex: "CalibrationPersistence").
///   Il survivra automatiquement à la transition de scène.
/// </summary>
public class CalibrationPersistence : MonoBehaviour
{
    public static CalibrationPersistence Instance { get; private set; }

    // ── Calibration VR (XR Origin VR) ──────────────────────────────────────
    public static Vector3    VROriginPosition { get; private set; }
    public static Quaternion VROriginRotation { get; private set; }
    public static bool       HasVRData        { get; private set; }

    // ── Calibration AR (XR Origin Mobile AR) ───────────────────────────────
    public static Vector3    AROriginPosition { get; private set; }
    public static Quaternion AROriginRotation { get; private set; }
    public static bool       HasARData        { get; private set; }

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

    // ── Sauvegarde ──────────────────────────────────────────────────────────

    /// <summary>Appelé par SceneTriggerVR (côté VR) avant la transition.</summary>
    public static void SaveVR(Transform xrOrigin)
    {
        if (xrOrigin == null) { Debug.LogWarning("[CalibrationPersistence] SaveVR : xrOrigin null !"); return; }
        VROriginPosition = xrOrigin.position;
        VROriginRotation = xrOrigin.rotation;
        HasVRData        = true;
        Debug.Log($"[CalibrationPersistence] VR sauvegardé → pos:{VROriginPosition}  rot:{VROriginRotation.eulerAngles}");
    }

    /// <summary>Appelé par SceneTriggerVR (côté AR) avant la transition.</summary>
    public static void SaveAR(Transform xrOrigin)
    {
        if (xrOrigin == null) { Debug.LogWarning("[CalibrationPersistence] SaveAR : xrOrigin null !"); return; }
        AROriginPosition = xrOrigin.position;
        AROriginRotation = xrOrigin.rotation;
        HasARData        = true;
        Debug.Log($"[CalibrationPersistence] AR sauvegardé → pos:{AROriginPosition}  rot:{AROriginRotation.eulerAngles}");
    }

    // ── Application ─────────────────────────────────────────────────────────

    /// <summary>Applique la calibration VR sauvegardée au XR Origin de TempleScene.</summary>
    public static void ApplyVR(Transform xrOriginTarget)
    {
        if (!HasVRData)
        {
            Debug.LogWarning("[CalibrationPersistence] ApplyVR : aucune donnée VR sauvegardée. Calibration non effectuée ?");
            return;
        }
        xrOriginTarget.position = VROriginPosition;
        xrOriginTarget.rotation = VROriginRotation;
        Debug.Log("[CalibrationPersistence] ✓ Calibration VR appliquée à TempleScene.");
    }

    /// <summary>Applique la calibration AR sauvegardée au XR Origin de TempleScene.</summary>
    public static void ApplyAR(Transform xrOriginTarget)
    {
        if (!HasARData)
        {
            Debug.LogWarning("[CalibrationPersistence] ApplyAR : aucune donnée AR sauvegardée. Calibration non effectuée ?");
            return;
        }
        xrOriginTarget.position = AROriginPosition;
        xrOriginTarget.rotation = AROriginRotation;
        Debug.Log("[CalibrationPersistence] ✓ Calibration AR appliquée à TempleScene.");
    }
}
