using UnityEngine;

public class PlatformManager : MonoBehaviour
{
    public GameObject vrRig;
    public GameObject arRig;

    void Start()
    {
        // Si on est sur un casque Meta (Android VR) ou dans l'éditeur en mode VR
        if (Application.platform == RuntimePlatform.Android && UnityEngine.XR.XRSettings.isDeviceActive)
        {
            vrRig.SetActive(true);
            arRig.SetActive(false);
            Debug.Log("Mode VR Activé");
        }
        else // Sinon on assume que c'est le téléphone AR
        {
            vrRig.SetActive(false);
            arRig.SetActive(true);
            Debug.Log("Mode AR Activé");
        }
    }
}