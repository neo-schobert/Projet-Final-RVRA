using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// À placer dans la SharedScene sur un GameObject dédié (ex: "XR Platform Setup").
/// Détecte la plateforme au runtime et active le bon rig XR :
///   - VR : active le XR Origin (VR)
///   - AR : active le AR Session + AR Session Origin
///
/// Les deux rigs doivent être présents dans la SharedScene et DÉSACTIVÉS par défaut.
/// </summary>
public class XRPlatformSetup : MonoBehaviour
{
    public static XRPlatformSetup Instance { get; private set; }

    [Header("VR — désactivé par défaut dans la scène")]
    [SerializeField] private GameObject _vrXROrigin;      // XR Origin (XR Rig) pour VR

    [Header("AR — désactivés par défaut dans la scène")]
    [SerializeField] private GameObject _arSession;        // AR Session
    [SerializeField] private GameObject _arSessionOrigin;  // AR Session Origin

    /// <summary>True si le joueur local est en VR, false si AR.</summary>
    public static bool IsVRMode { get; private set; }

    /// <summary>Transform du XR Origin VR — utilisé par VRPlayerController pour le suivi de position.</summary>
    public static Transform VRRig => Instance != null ? Instance._vrXROrigin?.transform : null;

    private void Awake()
    {
        // Singleton : une seule instance par scène
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        IsVRMode = DetectVR();

        if (IsVRMode)
        {
            _vrXROrigin?.SetActive(true);
            _arSession?.SetActive(false);
            _arSessionOrigin?.SetActive(false);
            Debug.Log("[XRPlatformSetup] Mode VR activé.");
        }
        else
        {
            _vrXROrigin?.SetActive(false);
            _arSession?.SetActive(true);
            _arSessionOrigin?.SetActive(true);
            Debug.Log("[XRPlatformSetup] Mode AR activé.");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Détecte si le joueur local utilise un casque VR opaque ou un device AR.
    /// - Aucun casque actif → AR (phone/tablet)
    /// - HoloLens / Magic Leap / ARCore / ARKit → AR
    /// - Tout autre casque XR (Oculus, Index, etc.) → VR
    /// </summary>
    private static bool DetectVR()
    {
        if (!XRSettings.isDeviceActive)
            return false; // pas de casque → device AR (phone/tablet)

        string deviceName = XRSettings.loadedDeviceName.ToLower();

        if (deviceName.Contains("hololens")  ||
            deviceName.Contains("magic leap") ||
            deviceName.Contains("arcore")     ||
            deviceName.Contains("arkit"))
            return false;

        return true; // casque opaque → VR
    }
}
