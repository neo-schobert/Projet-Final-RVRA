using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Calibration VR côté casque.
///
/// Le joueur VR se place physiquement sur le marqueur imprimé et appuie sur A.
///   1. Le XR Origin est déplacé pour que la position XZ de la tête = (0, ?, 0)
///   2. Le XR Origin est roté pour que la direction de regard = la même que le marqueur AR
///
/// Résultat : le casque et le téléphone partagent le même (0,0,0) physique.
///
/// Setup :
///   - Placer ce script sur un GameObject vide dans la scène (ex: "Calibration Manager")
///   - Assigner le XR Origin (XR Rig) et l'action de calibration (bouton A)
/// </summary>
public class VRCalibration : MonoBehaviour
{
    [Header("VR")]
    [Tooltip("Le Transform du XR Origin (XR Rig) dans ta scène")]
    [SerializeField] private Transform _xrOrigin;

    [Header("Input — bouton pour déclencher la calibration")]
    [Tooltip("Action Input System : ex. XRI RightHand/primaryButton (bouton A Oculus)")]
    [SerializeField] private InputActionReference _calibrateAction;

    [Header("Feedback visuel (optionnel)")]
    [Tooltip("Instructions visibles dans le casque : 'Place-toi sur le marqueur et appuie sur A'")]
    [SerializeField] private GameObject _instructionsUI;

    [Tooltip("Confirmation visible dans le casque : 'Calibré !'")]
    [SerializeField] private GameObject _calibratedUI;

    /// <summary>True une fois que le joueur a calibré.</summary>
    public static bool IsCalibrated { get; private set; }

    private void OnEnable()
    {
        if (_calibrateAction != null)
            _calibrateAction.action.performed += OnCalibratePressed;
    }

    private void OnDisable()
    {
        if (_calibrateAction != null)
            _calibrateAction.action.performed -= OnCalibratePressed;
    }

    private void Start()
    {
        IsCalibrated = false;
        _instructionsUI?.SetActive(true);
        _calibratedUI?.SetActive(false);

        // Active l'action au cas où elle ne serait pas activée
        _calibrateAction?.action.Enable();
    }

    private void OnCalibratePressed(InputAction.CallbackContext ctx)
    {
        if (IsCalibrated) return;
        Calibrate();
    }

    private void Calibrate()
    {
        Camera headCam = Camera.main;
        if (headCam == null)
        {
            Debug.LogError("[VRCalibration] Camera.main introuvable !");
            return;
        }

        // ── Étape 1 : Aligner la position XZ ───────────────────────────────────
        // La tête est au-dessus du marqueur → on aligne uniquement X et Z
        // (on ignore Y pour ne pas modifier la hauteur du rig)
        float headX = headCam.transform.position.x;
        float headZ = headCam.transform.position.z;

        _xrOrigin.position -= new Vector3(headX, 0f, headZ);

        // ── Étape 2 : Aligner le yaw (optionnel mais recommandé) ───────────────
        // Le joueur DOIT se tenir face à la même direction que le marqueur AR.
        // On annule le yaw actuel de la tête pour aligner les repères.
        float headYaw = headCam.transform.eulerAngles.y;
        _xrOrigin.Rotate(0f, -headYaw, 0f, Space.World);

        // ── Terminé ────────────────────────────────────────────────────────────
        IsCalibrated = true;

        _instructionsUI?.SetActive(false);
        _calibratedUI?.SetActive(true);

        Debug.Log($"[VRCalibration] ✓ Calibré ! XR Origin → position:{_xrOrigin.position} rotation:{_xrOrigin.eulerAngles}");
    }

    /// <summary>
    /// Alternative : appeler depuis un XR Interactable (bouton physique dans la scène)
    /// au lieu de l'Input System.
    /// </summary>
    public void CalibrateFromButton()
    {
        if (IsCalibrated) return;
        Calibrate();
    }
}
