using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class VRCalibration : MonoBehaviour
{
    [Header("VR")]
    [Tooltip("Le Transform du XR Origin (XR Rig) dans ta scène")]
    [SerializeField] private Transform _xrOrigin;

    [Header("Input — bouton pour déclencher la calibration")]
    [Tooltip("Action Input System : ex. XRI LeftHand/activate (trigger gauche)")]
    [SerializeField] private InputActionReference _calibrateAction;

    [Header("Feedback visuel")]
    [Tooltip("Instructions : 'Place-toi sur le marqueur et appuie sur le trigger'")]
    [SerializeField] private GameObject _instructionsUI;

    [Tooltip("Confirmation : 'Calibration réussie !' — disparaît après 5 secondes")]
    [SerializeField] private GameObject _calibratedUI;

    [Tooltip("Durée d'affichage du message de confirmation (secondes)")]
    [SerializeField] private float _calibratedDisplayDuration = 5f;

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
        _calibrateAction?.action.Enable();

        _instructionsUI?.SetActive(true);
        _calibratedUI?.SetActive(false);

        Debug.Log("[VRCalibration] Prêt. En attente du déclenchement...");
    }

    private void OnCalibratePressed(InputAction.CallbackContext ctx)
    {
        if (IsCalibrated) return;
        Debug.Log("[VRCalibration] Bouton reçu → calibration...");
        Calibrate();
    }

    private void Calibrate()
    {
        // ── Trouver la caméra ───────────────────────────────────────────────────
        Camera headCam = Camera.main;

        // Fallback : cherche par tag si Camera.main est null
        if (headCam == null)
        {
            var camObj = GameObject.FindWithTag("MainCamera");
            if (camObj != null) headCam = camObj.GetComponent<Camera>();
        }

        if (headCam == null)
        {
            Debug.LogError("[VRCalibration] Aucune camera MainCamera trouvée !");
            return;
        }

        Debug.Log($"[VRCalibration] Caméra trouvée : {headCam.name}");

        // ── Étape 1 : Aligner la position XZ ───────────────────────────────────
        float headX = headCam.transform.position.x;
        float headZ = headCam.transform.position.z;
        _xrOrigin.position -= new Vector3(headX, 0f, headZ);

        // ── Étape 2 : Aligner le yaw ────────────────────────────────────────────
        float headYaw = headCam.transform.eulerAngles.y;
        _xrOrigin.Rotate(0f, -headYaw, 0f, Space.World);

        // ── Terminé ─────────────────────────────────────────────────────────────
        IsCalibrated = true;

        Debug.Log($"[VRCalibration] _instructionsUI = {(_instructionsUI != null ? _instructionsUI.name : "NULL")}");
        Debug.Log($"[VRCalibration] _calibratedUI   = {(_calibratedUI != null ? _calibratedUI.name : "NULL")}");

        _instructionsUI?.SetActive(false);
        _calibratedUI?.SetActive(true);

        Debug.Log($"[VRCalibration] Instructions actif : {_instructionsUI?.activeSelf}");
        Debug.Log($"[VRCalibration] Calibré actif     : {_calibratedUI?.activeSelf}");

        // Le message disparaît après N secondes
        StartCoroutine(HideCalibratedUIAfterDelay());

        Debug.Log($"[VRCalibration] ✓ Calibré ! Origin → pos:{_xrOrigin.position} rot:{_xrOrigin.eulerAngles}");
    }

    private IEnumerator HideCalibratedUIAfterDelay()
    {
        yield return new WaitForSeconds(_calibratedDisplayDuration);
        _calibratedUI?.SetActive(false);
    }

    /// <summary>Appeler depuis un XR Interactable si besoin.</summary>
    public void CalibrateFromButton()
    {
        if (IsCalibrated) return;
        Calibrate();
    }
}
