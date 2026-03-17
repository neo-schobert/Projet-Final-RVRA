using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Zone de déclenchement de transition vers TempleScene.
/// Seul le joueur VR peut l'activer (XRRigSwitcher.IsVRMode).
///
/// FONCTIONNE EN SOLO ET EN MULTIJOUEUR :
///   • Solo (pas de session Netcode) → transition directe via SceneManager Unity standard.
///   • Multijoueur (session Netcode active) → RPC broadcast, session owner charge la scène.
///
/// CORRECTIFS :
///   A. Distance XZ seulement (cylindre, pas sphère) :
///      le trigger est au sol (~0.4 m) mais la tête VR est à ~1.7 m.
///      En sphère 3D, la différence Y seule dépasse le rayon → jamais en zone.
///   B. IsSpawned ne bloque plus la détection de proximité ni le prompt UI.
///   C. Input via événement performed (plus robuste que WasPressedThisFrame).
///   D. Fallback solo : si pas de session Netcode, transition purement locale.
///
/// SETUP dans Scene.unity :
///   1. Créer un GameObject vide "SceneTrigger" et y attacher ce script.
///   2. Ajouter un NetworkObject sur ce même GameObject.
///   3. _activateAction → XRI Right Interaction/Select (ou autre bouton VR).
///   4. _promptUI      → le GameObject "VR_Trigger" (texte 3D, désactivé par défaut).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class SceneTriggerVR : NetworkBehaviour
{
    [Header("Activation")]
    [Tooltip("Rayon horizontal (XZ) en mètres dans lequel le joueur VR peut activer le trigger.")]
    [SerializeField] private float _activationRadius = 1f;

    [Tooltip("Action Input System (ex. XRI Right Interaction/Select).")]
    [SerializeField] private InputActionReference _activateAction;

    [Header("UI Prompt")]
    [Tooltip("GameObject affiché quand le joueur VR est à portée (ex. VR_Trigger text 3D).")]
    [SerializeField] private GameObject _promptUI;

    [Header("Scène cible")]
    [Tooltip("Nom exact de la scène à charger (doit être dans Build Settings).")]
    [SerializeField] private string _targetSceneName = "TempleScene";

    // ─────────────────────────────────────────────────────────────────────────

    private bool _inRange      = false;
    private bool _hasTriggered = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (_activateAction != null)
        {
            _activateAction.action.Enable();
            _activateAction.action.performed += OnActivateActionPerformed;
        }
    }

    private void OnDisable()
    {
        if (_activateAction != null)
            _activateAction.action.performed -= OnActivateActionPerformed;
    }

    // ── Update : proximité + prompt UI ───────────────────────────────────────
    // Pas de garde IsSpawned ici : le prompt fonctionne avec ou sans Netcode.

    private void Update()
    {
        if (!XRRigSwitcher.IsVRMode) return;

        Camera head = Camera.main;
        if (head == null) return;

        // Distance horizontale XZ uniquement (cylindre, pas sphère).
        // Raison : le trigger est au sol (~0.4 m) et la tête est à ~1.7 m.
        // La différence Y seule dépasse le rayon → jamais "en zone" en 3D.
        float dx     = transform.position.x - head.transform.position.x;
        float dz     = transform.position.z - head.transform.position.z;
        float distXZ = Mathf.Sqrt(dx * dx + dz * dz);
        bool  inRange = distXZ <= _activationRadius;

        if (inRange != _inRange)
        {
            _inRange = inRange;
            if (_promptUI != null)
                _promptUI.SetActive(_inRange);

            Debug.Log(_inRange
                ? $"[SceneTriggerVR] ✓ Dans la zone (distXZ={distXZ:F2}m ≤ {_activationRadius}m)."
                : $"[SceneTriggerVR] Sorti de la zone (distXZ={distXZ:F2}m).");
        }
    }

    // ── Input (événement) ─────────────────────────────────────────────────────

    private void OnActivateActionPerformed(InputAction.CallbackContext ctx)
    {
        if (!XRRigSwitcher.IsVRMode)    return;
        if (!_inRange || _hasTriggered) return;

        OnActivatePressed();
    }

    // ── Déclenchement ─────────────────────────────────────────────────────────

    private void OnActivatePressed()
    {
        _hasTriggered = true;
        if (_promptUI != null) _promptUI.SetActive(false);

        Debug.Log("[SceneTriggerVR] 🚀 Trigger activé !");

        // ── Choisir le chemin selon la présence d'une session Netcode ────────
        bool sessionActive = NetworkManager.Singleton != null
                          && NetworkManager.Singleton.IsConnectedClient
                          && IsSpawned;

        if (sessionActive)
        {
            // Multijoueur : on diffuse à tous via RPC.
            // Chaque client sauvegarde sa calibration ; le session owner charge la scène.
            Debug.Log("[SceneTriggerVR] Session Netcode détectée → chemin multijoueur (RPC).");
            DemanderTransitionRpc();
        }
        else
        {
            // Solo : pas de session (ou session non encore synchronisée).
            // On sauvegarde localement et on charge directement avec SceneManager Unity.
            Debug.Log("[SceneTriggerVR] Pas de session Netcode → chemin solo (SceneManager direct).");
            SauvegarderCalibrationLocale();
            SceneManager.LoadScene(_targetSceneName);
        }
    }

    // ── RPC broadcast (multijoueur DA) ────────────────────────────────────────

    [Rpc(SendTo.Everyone)]
    private void DemanderTransitionRpc()
    {
        Debug.Log("[SceneTriggerVR] RPC reçu — sauvegarde calibration locale...");

        // Chaque client sauvegarde son propre XR Origin
        SauvegarderCalibrationLocale();

        // Seul le session owner (clientId 0) appelle LoadScene en DA
        if (NetworkManager.Singleton.LocalClientId == NetworkManager.ServerClientId)
        {
            Debug.Log($"[SceneTriggerVR] Session owner → LoadScene(\"{_targetSceneName}\")");
            NetworkManager.Singleton.SceneManager.LoadScene(_targetSceneName, LoadSceneMode.Single);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Sauvegarde la calibration du XR Origin actif (VR ou AR).</summary>
    private static void SauvegarderCalibrationLocale()
    {
        Transform origin = TrouverXROriginActif();
        if (origin == null) return;

        if (XRRigSwitcher.IsVRMode)
            CalibrationPersistence.SaveVR(origin);
        else
            CalibrationPersistence.SaveAR(origin);
    }

    /// <summary>Renvoie le Transform du XROrigin actif (ignore les rigs désactivés).</summary>
    private static Transform TrouverXROriginActif()
    {
        XROrigin origin = FindAnyObjectByType<XROrigin>();
        if (origin != null) return origin.transform;

        Debug.LogWarning("[SceneTriggerVR] FindAnyObjectByType<XROrigin>() : aucun résultat. " +
                         "Vérifie que le XR Origin actif est bien dans la scène.");
        return null;
    }

    // ── Gizmos éditeur ────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Sphère semi-transparente (représentation visuelle du cylindre XZ)
        Gizmos.color = new Color(0f, 1f, 0.45f, 0.20f);
        Gizmos.DrawSphere(transform.position, _activationRadius);

        Gizmos.color = new Color(0f, 1f, 0.45f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, _activationRadius);

        // Ligne verticale pour repérer la zone depuis l'éditeur
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2.2f);
    }
}
