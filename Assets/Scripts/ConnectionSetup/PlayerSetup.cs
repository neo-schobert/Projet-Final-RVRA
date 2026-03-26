using System.Collections;
using Unity.Netcode;
using Unity.XR.CoreUtils;
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
        if (!IsOwner) return;

        _cam = Camera.main;
        if (_cam != null)
        {
            AppliquerPosition();
            Debug.Log($"[PlayerSetup] Joueur local prêt → suivi de : {_cam.name}");
        }
        else
        {
            Debug.LogWarning("[PlayerSetup] Camera.main introuvable au spawn — " +
                             "nouvel essai dans Update().");
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return;
            Debug.Log($"[PlayerSetup] Caméra trouvée en Update() → {_cam.name}");
        }

        AppliquerPosition();
    }

    /// <summary>
    /// Suit Camera.main XZ à Y=0 (position pied du joueur dans l'espace virtuel).
    /// Camera.main = position réelle du joueur = XR Origin + ARCore localPosition.
    /// C'est la cible correcte : elle bouge avec le joueur physique.
    /// L'offset entre Camera.main et XR Origin est normal (décalage ARCore depuis
    /// le début de session) — XR Origin est une ancre fixe, pas la position du joueur.
    /// </summary>
    private void AppliquerPosition()
    {
        Vector3 camPos = _cam.transform.position;
        transform.position = new Vector3(camPos.x, 0f, camPos.z);
        transform.rotation = Quaternion.Euler(0f, _cam.transform.eulerAngles.y, 0f);
    }


    // ── Synchronisation calibration ───────────────────────────────────────────
    // ARProximityCalibration est un MonoBehaviour sans accès aux RPCs.
    // Il passe par ce NetworkBehaviour pour diffuser les données de calibration
    // à TOUS les clients (y compris VR) dès qu'une calibration réussit.
    // Sans ce sync, CalibrationPersistence.HasData reste false sur VR →
    // TempleSceneManager ne peut pas positionner les joueurs ni orienter le gameboard.

    /// <summary>
    /// Appelé depuis ARProximityCalibration juste après la calibration.
    /// Diffuse les 4 valeurs fondamentales à tous les clients (VR + AR).
    /// </summary>
    public void SyncCalibrationToAll(float vrYaw, Vector3 vrPosition,
                                     float arYaw, Vector3 arPosition)
    {
        SyncCalibrationRpc(vrYaw, vrPosition, arYaw, arPosition);
    }

    // InvokePermission.Everyone : AR n'est PAS owner du PlayerSetup VR.
    // Sans cette permission, le RPC envoyé depuis AR est silencieusement ignoré
    // → HasData reste false sur VR → gameboard non orienté, joueurs mal positionnés.
    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    private void SyncCalibrationRpc(float vrYaw, Vector3 vrPosition,
                                    float arYaw, Vector3 arPosition)
    {
        CalibrationPersistence.SaveCalibration(vrYaw, vrPosition, arYaw, arPosition);
        Debug.Log($"[PlayerSetup] ✓ CalibrationSync reçu — " +
                  $"vrYaw={vrYaw:F1}°  vrPos={vrPosition:F3}  " +
                  $"arYaw={arYaw:F1}°  arPos={arPosition:F3}");
    }

    /// <summary>
    /// Appelé depuis ARProximityCalibration.OnLancerJeuButtonPressed juste avant la vue aérienne.
    /// Diffuse la position/yaw AR finale à tous les clients (y compris VR),
    /// pour que TempleSceneManager place correctement le joueur AR et aligne le gameboard.
    /// </summary>
    public void SyncFinalARSpawnToAll(float arYaw, Vector3 arPosition)
    {
        SyncFinalARSpawnRpc(arYaw, arPosition);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    private void SyncFinalARSpawnRpc(float arYaw, Vector3 arPosition)
    {
        CalibrationPersistence.UpdateAR(arYaw, arPosition);
        Debug.Log($"[PlayerSetup] ✓ FinalARSpawn reçu — arYaw={arYaw:F1}°  arPos={arPosition:F3}");
    }

    private IEnumerator HideAfterDelay(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (obj != null) obj.SetActive(false);
    }
}
