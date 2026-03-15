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
public class PlayerSetup : NetworkBehaviour
{
    private Camera _cam;

    public override void OnNetworkSpawn()
    {
        // IsOwner fonctionne en mode Relay (hôte/client) ET en mode DA
        if (!IsOwner) return;

        _cam = Camera.main;

        if (_cam == null)
        {
            Debug.LogError("[PlayerSetup] Camera.main introuvable ! Vérifie le tag MainCamera sur ta caméra active.");
            return;
        }

        // Position initiale = position actuelle de la caméra
        transform.position = _cam.transform.position;
        transform.rotation = Quaternion.Euler(0f, _cam.transform.eulerAngles.y, 0f);

        Debug.Log($"[PlayerSetup] Joueur local prêt → suivi de : {_cam.name}");
    }

    private void Update()
    {
        // Seulement pour le joueur local (celui dont on est propriétaire)
        if (!IsOwner || _cam == null) return;

        // Suit la position de la tête (camera) exactement
        transform.position = _cam.transform.position;

        // Pour le corps : seulement le yaw (rotation horizontale)
        // Le pitch (haut/bas) et le roll ne s'appliquent pas à un corps debout
        float yaw = _cam.transform.eulerAngles.y;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }
}
