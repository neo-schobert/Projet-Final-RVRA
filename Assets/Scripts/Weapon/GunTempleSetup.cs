using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// À mettre sur le gun dans TempleScene (même GameObject que Shoot.cs).
///
/// Responsabilités :
///   1. Awake() — lit WasRightHand et désactive XRGrabInteractable AVANT tout Start().
///      → Empêche le Near-Far Interactor (parent du gun) d'auto-sélectionner le gun via XRI,
///        ce qui déclencherait Shoot.OnGrabbed() et écraserait GunPersistence.WasRightHand.
///   2. Start() — reparente le gun dans la bonne main, réclame l'ownership NGO,
///        s'abonne à l'action de tir et appelle Shoot.FireLocal() à chaque pression.
///
/// SETUP Inspector :
///   • rightHandParent → glisser "Right Controller" (ou l'attach point main droite)
///   • leftHandParent  → glisser "Left Controller"  (ou l'attach point main gauche)
///   • shootActionRight → XRI Right Hand Interaction/Activate
///   • shootActionLeft  → XRI Left Hand Interaction/Activate
/// </summary>
public class GunTempleSetup : MonoBehaviour
{
    [Header("Parents des mains (glisser depuis la hiérarchie)")]
    [Tooltip("Transform parent pour la main droite (ex : Right Controller)")]
    public Transform rightHandParent;

    [Tooltip("Transform parent pour la main gauche (ex : Left Controller)")]
    public Transform leftHandParent;

    [Header("Actions de tir")]
    [Tooltip("Action trigger main droite — ex : XRI Right Hand Interaction/Activate")]
    public InputActionReference shootActionRight;

    [Tooltip("Action trigger main gauche — ex : XRI Left Hand Interaction/Activate")]
    public InputActionReference shootActionLeft;

    [Header("Offset dans la main (optionnel)")]
    public Vector3 localPositionInHand = Vector3.zero;
    public Vector3 localRotationInHand = Vector3.zero;

    private InputActionReference _activeAction;
    private Shoot                _shoot;

    // WasRightHand lu en Awake() pour être protégé contre toute écriture
    // ultérieure par Shoot.OnGrabbed() (qui peut être déclenché par XRI
    // si XRGrabInteractable n'est pas encore désactivé au moment des Start()).
    private bool _rightHand;

    // ── Awake : priorité maximale ─────────────────────────────────────────────
    // Awake() de TOUS les scripts s'exécute avant le premier Start() de n'importe quel script.
    // On en profite pour :
    //   1. Capturer WasRightHand avant que quoi que ce soit ne puisse l'écraser.
    //   2. Désactiver XRGrabInteractable → le Near-Far Interactor du contrôleur
    //      (parent du gun) ne peut plus auto-sélectionner le gun → Shoot.OnGrabbed()
    //      ne sera jamais appelé en TempleScene → WasRightHand reste intact.
    private void Awake()
    {
        if (!XRRigSwitcher.IsVRMode) return;

        // 1. Capture de WasRightHand
        _rightHand = GunPersistence.WasRightHand;
        Debug.Log($"[GunTempleSetup] Awake — WasRightHand={_rightHand}  WasHeld={GunPersistence.WasHeld}");

        // 2. Désactivation de XRGrabInteractable
        var grabbable = GetComponent<XRGrabInteractable>();
        if (grabbable != null)
        {
            grabbable.enabled = false;
            Debug.Log("[GunTempleSetup] XRGrabInteractable désactivé — auto-grab XRI empêché.");
        }
        else
        {
            Debug.LogWarning("[GunTempleSetup] XRGrabInteractable introuvable sur ce GameObject.");
        }
    }

    private void Start()
    {
        if (!XRRigSwitcher.IsVRMode) return;

        _shoot = GetComponent<Shoot>();
        if (_shoot == null)
        {
            Debug.LogError("[GunTempleSetup] Shoot introuvable sur ce GameObject !");
            return;
        }

        // ── 0. Désactiver GunAutoGrab ─────────────────────────────────────────
        // GunAutoGrab est conçu pour la scène principale (XRGrabInteractable).
        // Dans TempleScene le gun est fixé dans la main — GunAutoGrab ne doit
        // pas tourner : il interfèrerait avec l'action de tir et réinitialiserait
        // WasRightHand=true via GunPersistence.Reset() à la fin de sa coroutine.
        var autoGrab = GetComponent<GunAutoGrab>();
        if (autoGrab != null) autoGrab.enabled = false;

        Debug.Log($"[GunTempleSetup] Start — main {(_rightHand ? "droite" : "gauche")}");

        // ── 1. Ownership NGO ──────────────────────────────────────────────────
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned && !netObj.IsOwner)
        {
            netObj.ChangeOwnership(NetworkManager.Singleton.LocalClientId);
            Debug.Log("[GunTempleSetup] Ownership réclamé pour le joueur VR local.");
        }

        // ── 2. Reparenter dans la bonne main ──────────────────────────────────
        Transform handParent = _rightHand ? rightHandParent : leftHandParent;
        if (handParent != null)
        {
            transform.SetParent(handParent, false);
            transform.localPosition = localPositionInHand;
            transform.localRotation = Quaternion.Euler(localRotationInHand);
            Debug.Log($"[GunTempleSetup] ✓ Gun reparenté dans '{handParent.name}' " +
                      $"(main {(_rightHand ? "droite" : "gauche")}).");
        }
        else
        {
            Debug.LogWarning($"[GunTempleSetup] Parent main {(_rightHand ? "droite" : "gauche")} " +
                             "non assigné dans l'Inspector — gun laissé dans son parent actuel.");
        }

        // ── 3. Action de tir ──────────────────────────────────────────────────
        _activeAction = _rightHand ? shootActionRight : shootActionLeft;
        if (_activeAction == null)
        {
            Debug.LogError($"[GunTempleSetup] shootAction{(_rightHand ? "Right" : "Left")} " +
                           "non assigné dans l'Inspector !");
            return;
        }

        _activeAction.action.started += OnShootInput;
        _activeAction.action.Enable();
        Debug.Log($"[GunTempleSetup] ✓ Action '{_activeAction.action.name}' activée — prêt à tirer.");
    }

    private void OnDestroy()
    {
        if (_activeAction != null)
            _activeAction.action.started -= OnShootInput;
    }

    private void OnShootInput(InputAction.CallbackContext ctx)
    {
        Debug.Log("[GunTempleSetup] OnShootInput → FireLocal()");
        _shoot?.FireLocal();
    }
}
