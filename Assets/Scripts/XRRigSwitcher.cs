using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Cas 2 — scène partagée.
/// Place ce script sur un GameObject vide dans ta scène.
/// Il détecte la plateforme au démarrage et active le bon rig XR,
/// tout en désactivant l'autre.
///
/// Dans ta scène, les deux rigs doivent exister mais être DÉSACTIVÉS par défaut.
/// </summary>
public class XRRigSwitcher : MonoBehaviour
{
    public static XRRigSwitcher Instance { get; private set; }

    [Header("Rig VR (ex: XR Origin avec controllers) — désactivé par défaut")]
    [SerializeField] private GameObject vrXROrigin;

    [Header("Rig AR (téléphone) — désactivés par défaut")]
    [SerializeField] private GameObject arSession;
    [SerializeField] private GameObject arSessionOrigin;

    /// <summary>True = casque VR opaque, False = téléphone/tablette AR</summary>
    public static bool IsVRMode { get; private set; }

    private void Awake()
    {
        Instance = this;
        IsVRMode = DetectVR();

        if (IsVRMode)
        {
            vrXROrigin?.SetActive(true);
            arSession?.SetActive(false);
            arSessionOrigin?.SetActive(false);
            Debug.Log("[XRRigSwitcher] Casque VR détecté → XR Origin activé.");
        }
        else
        {
            vrXROrigin?.SetActive(false);
            arSession?.SetActive(true);
            arSessionOrigin?.SetActive(true);
            Debug.Log("[XRRigSwitcher] Device AR détecté → AR Session activée.");
        }
    }

    /// <summary>
    /// Détecte la plateforme :
    ///  - Aucun casque actif  → AR (phone/tablet)
    ///  - HoloLens/ARKit/etc  → AR
    ///  - Oculus/Index/etc    → VR
    /// </summary>
    private static bool DetectVR()
    {
        if (!XRSettings.isDeviceActive)
            return false;

        string deviceName = XRSettings.loadedDeviceName.ToLower();

        if (deviceName.Contains("hololens")  ||
            deviceName.Contains("magic leap") ||
            deviceName.Contains("arcore")     ||
            deviceName.Contains("arkit"))
            return false;

        return true;
    }
}
