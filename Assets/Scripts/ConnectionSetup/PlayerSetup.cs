using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// À mettre sur les prefabs PlayerVR et PlayerAR.
///
/// Joueur local (HasAuthority) :
///   → Suit Camera.main dans Update() chaque frame.
///   → ClientNetworkTransform envoie cette position aux autres clients.
///
/// Joueur distant :
///   → ClientNetworkTransform applique la position reçue du réseau.
///
/// IMPORTANT : ne pas utiliser SetParent(camera) car ClientNetworkTransform
/// et le parenting se battent → corps figé. On utilise Update() à la place.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(ClientNetworkTransform))]
public class PlayerSetup : NetworkBehaviour
{
    [Header("Message début de jeu (VR seulement)")]
    [Tooltip("GameObject à afficher sur le casque VR quand l'AR lance le jeu. " +
             "Assigner uniquement sur le prefab PlayerVR.")]
    [SerializeField] private GameObject _startGameMessage;

    private Camera _cam;

    public override void OnNetworkSpawn()
    {
        // IsOwner fonctionne en mode Relay (hôte/client) ET en mode DA
        if (!IsOwner) return;

        // On tente de trouver Camera.main immédiatement, mais si elle n'est pas
        // encore disponible (ex: caméra AR Foundation pas encore initialisée),
        // Update() réessaiera chaque frame jusqu'à la trouver.
        _cam = Camera.main;

        if (_cam != null)
        {
            transform.position = _cam.transform.position - new Vector3(0f, _cam.transform.position.y, 0f);
            transform.rotation = Quaternion.Euler(0f, _cam.transform.eulerAngles.y, 0f);
            Debug.Log($"[PlayerSetup] Joueur local prêt → suivi de : {_cam.name}");
        }
        else
        {
            Debug.LogWarning("[PlayerSetup] Camera.main introuvable au spawn — " +
                             "nouvel essai chaque frame dans Update(). " +
                             "Vérifie le tag MainCamera sur ta caméra active.");
        }
    }

    private void Update()
    {
        // Seulement pour le joueur local (celui dont on est propriétaire)
        if (!IsOwner) return;

        // Réessaye de trouver Camera.main si elle n'était pas disponible au spawn.
        // Cas typique : caméra AR Foundation initialisée quelques frames après Netcode.
        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return; // pas encore disponible, on réessaie à la prochaine frame

            // Première fois qu'on trouve la caméra : position initiale
            transform.position = _cam.transform.position - new Vector3(0f, _cam.transform.position.y, 0f);
            transform.rotation = Quaternion.Euler(0f, _cam.transform.eulerAngles.y, 0f);
            Debug.Log($"[PlayerSetup] Caméra trouvée en Update() → suivi de : {_cam.name}");
        }

        // Suit la position de la tête (camera) exactement
        transform.position = _cam.transform.position - new Vector3(0f, _cam.transform.position.y, 0f);

        // Pour le corps : seulement le yaw (rotation horizontale)
        float yaw = _cam.transform.eulerAngles.y;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }


    // ── Synchronisation calibration ───────────────────────────────────────────
    // ARProximityCalibration est un MonoBehaviour sans accès aux RPCs.
    // Il passe par ce NetworkBehaviour pour diffuser les données de calibration
    // à TOUS les clients (y compris VR) dès qu'une calibration réussit.
    // Sans ce sync, CalibrationPersistence.HasData reste false sur VR →
    // TempleSceneManager ne peut pas positionner les joueurs ni orienter le gameboard.

    /// <summary>
    /// Appelé depuis ARProximityCalibration juste après SaveCalibration.
    /// Envoie les données de calibration à tous les clients via RPC.
    /// </summary>
    public void SyncCalibrationToAll(float vrYaw, Quaternion xrOriginRot,
                                     Vector3 camLocalPos, float arCamYaw)
    {
        SyncCalibrationRpc(vrYaw, xrOriginRot, camLocalPos, arCamYaw);
    }

    // InvokePermission.Everyone : AR n'est PAS owner du PlayerSetup VR.
    // Sans cette permission, le RPC envoyé depuis AR est silencieusement ignoré
    // → HasData reste false sur VR → gameboard non orienté, joueurs mal positionnés.
    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    private void SyncCalibrationRpc(float vrYaw, Quaternion xrOriginRot,
                                    Vector3 camLocalPos, float arCamYaw)
    {
        CalibrationPersistence.SaveCalibration(vrYaw, xrOriginRot, camLocalPos, arCamYaw);
        Debug.Log($"[PlayerSetup] ✓ CalibrationSync reçu — vrYaw={vrYaw:F1}° | " +
                  $"xrOriginRot={xrOriginRot.eulerAngles:F1}° | " +
                  $"camLocalPos={camLocalPos:F3} | arCamYaw={arCamYaw:F1}°");
    }

    /// <summary>
    /// Appelé depuis ARProximityCalibration.OnLancerJeuButtonPressed juste avant la vue aérienne.
    /// Diffuse la position/rotation AR finale à tous les clients (y compris VR),
    /// pour que TempleSceneManager place correctement le joueur AR et aligne le gameboard.
    /// </summary>
    public void SyncFinalARSpawnToAll(Vector3 arPosition, Quaternion arRotation)
    {
        SyncFinalARSpawnRpc(arPosition, arRotation);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    private void SyncFinalARSpawnRpc(Vector3 arPosition, Quaternion arRotation)
    {
        CalibrationPersistence.SaveFinalARSpawn(arPosition, arRotation);
        Debug.Log($"[PlayerSetup] ✓ FinalARSpawn reçu — pos={arPosition:F3} | rot={arRotation.eulerAngles:F1}°");
    }

    private IEnumerator HideAfterDelay(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (obj != null) obj.SetActive(false);
    }
}
