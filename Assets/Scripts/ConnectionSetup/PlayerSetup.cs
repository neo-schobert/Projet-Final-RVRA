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
}
