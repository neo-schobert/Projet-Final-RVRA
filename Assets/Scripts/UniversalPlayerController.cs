using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Contrôleur joueur universel (VR + AR).
///
/// IMPORTANT : on NE fait PAS SetParent(camera) car ClientNetworkTransform
/// et le parenting se battent → corps figé côté distant.
/// On suit Camera.main dans Update() à la place (même logique que PlayerSetup).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(ClientNetworkTransform))]
public class UniversalPlayerController : NetworkBehaviour
{
    private Camera _cam;

    public override void OnNetworkSpawn()
    {
        // Fonctionne en mode Relay (hôte/client) ET en mode DA
        if (!IsOwner) return;

        // On tente de trouver Camera.main immédiatement, mais si elle n'est pas
        // encore disponible (ex: caméra AR Foundation pas encore initialisée),
        // Update() réessaiera chaque frame jusqu'à la trouver.
        _cam = Camera.main;

        if (_cam != null)
        {
            transform.position = _cam.transform.position;
            transform.rotation = Quaternion.Euler(0f, _cam.transform.eulerAngles.y, 0f);
            Debug.Log($"[UniversalPlayerController] Joueur local prêt → suivi de : {_cam.name}");
        }
        else
        {
            Debug.LogWarning("[UniversalPlayerController] Camera.main introuvable au spawn — " +
                             "nouvel essai chaque frame dans Update(). " +
                             "Vérifie le tag MainCamera sur ta caméra active.");
        }
    }

    private void Update()
    {
        // Seulement pour le joueur local (propriétaire)
        // Le joueur distant reçoit sa position via ClientNetworkTransform
        if (!IsOwner) return;

        // Réessaye de trouver Camera.main si elle n'était pas disponible au spawn.
        // Cas typique : caméra AR Foundation initialisée quelques frames après Netcode.
        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return; // pas encore disponible, on réessaie à la prochaine frame

            // Première fois qu'on trouve la caméra : position initiale
            transform.position = _cam.transform.position;
            transform.rotation = Quaternion.Euler(0f, _cam.transform.eulerAngles.y, 0f);
            Debug.Log($"[UniversalPlayerController] Caméra trouvée en Update() → suivi de : {_cam.name}");
        }

        // Suit la position de la tête exactement
        transform.position = _cam.transform.position;

        // Seulement le yaw (rotation horizontale) — pas de pitch/roll sur un corps debout
        float yaw = _cam.transform.eulerAngles.y;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }
}