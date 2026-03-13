using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Calibration AR côté téléphone.
///
/// Quand ARKit détecte l'image imprimée (le marqueur) :
///   1. L'AR Session Origin est roté pour annuler la rotation du marqueur
///   2. L'AR Session Origin est déplacé pour que le marqueur soit à (0,0,0)
///
/// Résultat : le téléphone et le casque partagent le même (0,0,0) physique.
///
/// Setup :
///   - Placer ce script sur un GameObject vide dans la scène (ex: "Calibration Manager")
///   - Assigner ARTrackedImageManager (sur le AR Session Origin) et le AR Session Origin
/// </summary>
public class ARCalibration : MonoBehaviour
{
    [Header("AR Foundation")]
    [Tooltip("ARTrackedImageManager présent sur ton AR Session Origin")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;

    [Tooltip("Le Transform du AR Session Origin (ou XR Origin AR)")]
    [SerializeField] private Transform _arSessionOrigin;

    [Header("Feedback visuel (optionnel)")]
    [Tooltip("Un texte/UI qui indique 'Cherche le marqueur...' → disparaît après calibration")]
    [SerializeField] private GameObject _searchingUI;

    [Tooltip("Un texte/UI qui indique 'Calibré !' → apparaît après calibration")]
    [SerializeField] private GameObject _calibratedUI;

    /// <summary>True une fois que le marqueur a été détecté et la calibration effectuée.</summary>
    public static bool IsCalibrated { get; private set; }

    private void OnEnable()
    {
        _trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
    }

    private void OnDisable()
    {
        _trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
    }

    private void Start()
    {
        IsCalibrated = false;
        _searchingUI?.SetActive(true);
        _calibratedUI?.SetActive(false);
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
    {
        if (IsCalibrated) return;

        // Tente de calibrer sur les images nouvellement détectées
        foreach (var image in args.added)
            TryCalibrate(image);

        // Et sur les images déjà suivies (au cas où elle bougent légèrement au début)
        foreach (var image in args.updated)
            if (image.trackingState == TrackingState.Tracking)
                TryCalibrate(image);
    }

    private void TryCalibrate(ARTrackedImage image)
    {
        if (IsCalibrated) return;

        // ── Étape 1 : Aligner la rotation (yaw uniquement) ─────────────────────
        // Le marqueur est posé à plat sur une surface → on n'aligne que le yaw (Y)
        float markerYaw = image.transform.eulerAngles.y;
        _arSessionOrigin.rotation = Quaternion.Euler(0f, -markerYaw, 0f);

        // ── Étape 2 : Aligner la position ──────────────────────────────────────
        // Après la rotation, image.transform.position est recalculé automatiquement
        // par Unity. On déplace l'origine pour que le marqueur soit à (0, 0, 0).
        _arSessionOrigin.position -= image.transform.position;

        // ── Terminé ────────────────────────────────────────────────────────────
        IsCalibrated = true;

        _searchingUI?.SetActive(false);
        _calibratedUI?.SetActive(true);

        Debug.Log($"[ARCalibration] ✓ Calibré ! Marqueur '{image.referenceImage.name}' → (0,0,0)");
        Debug.Log($"[ARCalibration] AR Origin → position:{_arSessionOrigin.position} rotation:{_arSessionOrigin.eulerAngles}");
    }
}
