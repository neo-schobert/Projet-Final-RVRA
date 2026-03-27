using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// À mettre sur le gun dans TempleScene (même GameObject que Shoot.cs).
///
/// Responsabilités :
///   1. Awake() — lit WasRightHand ET désactive XRGrabInteractable ET désactive GunAutoGrab,
///      le TOUT avant que n'importe quel Start() s'exécute.
///      → Empêche GunAutoGrab.Start() de lancer sa coroutine (Unity n'appelle jamais
///        Start() sur un composant désactivé).
///      → Empêche XRI d'auto-sélectionner le gun via le Near-Far Interactor.
///   2. Start() — reparente le gun dans la bonne main, réclame l'ownership NGO,
///      lit les InputActionReferences depuis Shoot.cs (plus besoin de les ré-assigner).
///
/// SETUP Inspector :
///   • rightHandParent → glisser "Right Controller" (ou l'attach point main droite)
///   • leftHandParent  → glisser "Left Controller"  (ou l'attach point main gauche)
///   • (shootActionRight / shootActionLeft sont lus automatiquement depuis Shoot.cs)
/// </summary>
public class GunTempleSetup : MonoBehaviour
{
    [Header("Parents des mains (glisser depuis la hiérarchie)")]
    [Tooltip("Transform parent pour la main droite (ex : Right Controller)")]
    public Transform rightHandParent;

    [Tooltip("Transform parent pour la main gauche (ex : Left Controller)")]
    public Transform leftHandParent;

    [Header("Offset dans la main (optionnel)")]
    public Vector3 localPositionInHand = Vector3.zero;
    public Vector3 localRotationInHand = Vector3.zero;

    private InputActionReference _activeAction;
    private Shoot                _shoot;

    // WasRightHand lu en Awake() — protégé contre toute écriture ultérieure
    private bool _rightHand;

    // ── Awake : priorité maximale ─────────────────────────────────────────────
    // • Toutes les Awake() s'exécutent avant TOUS les Start().
    // • Désactiver GunAutoGrab ICI (et non dans Start) garantit que
    //   GunAutoGrab.Start() ne sera JAMAIS appelé par Unity → sa coroutine
    //   ne démarre pas → pas de Reset() ni de SelectEnter() parasites.
    private void Awake()
    {
        if (!XRRigSwitcher.IsVRMode) return;

        // 1. Capture de WasRightHand
        _rightHand = GunPersistence.WasRightHand;
        Debug.Log($"[GunTempleSetup] Awake — WasRightHand={_rightHand}  WasHeld={GunPersistence.WasHeld}");

        // 2. Désactivation de XRGrabInteractable → XRI ne peut plus auto-sélectionner le gun
        var grabbable = GetComponent<XRGrabInteractable>();
        if (grabbable != null)
        {
            grabbable.enabled = false;
            Debug.Log("[GunTempleSetup] XRGrabInteractable désactivé.");
        }

        // 3. Désactivation de GunAutoGrab AVANT son Start()
        //    → Unity n'appelle jamais Start() sur un composant désactivé.
        //    → Sa coroutine ne démarrera jamais → GunPersistence.Reset() ne sera jamais appelé.
        var autoGrab = GetComponent<GunAutoGrab>();
        if (autoGrab != null)
        {
            autoGrab.enabled = false;
            Debug.Log("[GunTempleSetup] GunAutoGrab désactivé dès Awake().");
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
            Debug.Log($"[GunTempleSetup] ✓ Gun reparenté dans '{handParent.name}'.");
        }
        else
        {
            Debug.LogWarning($"[GunTempleSetup] Parent main {(_rightHand ? "droite" : "gauche")} " +
                             "non assigné dans l'Inspector !");
        }

        // ── 3. Action de tir — lue depuis Shoot.cs (pas besoin de re-assigner) ─
        // Shoot.cs possède déjà shootActionRight et shootActionLeft assignés dans l'Inspector.
        // On les réutilise directement : une seule source de vérité, zéro duplication.
        _activeAction = _rightHand ? _shoot.shootActionRight : _shoot.shootActionLeft;

        if (_activeAction == null)
        {
            Debug.LogError($"[GunTempleSetup] Shoot.shootAction{(_rightHand ? "Right" : "Left")} " +
                           "est null ! Assigne-le dans l'Inspector sur Shoot.cs.");
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
