using UnityEngine;
using Unity.Netcode;

public class UniversalPlayerController : NetworkBehaviour
{
    private Transform cameraTransform;

    public override void OnNetworkSpawn()
    {
        if (HasAuthority)
        {
            TrouverCamera();
            
            // On cache la sphère pour nous-mêmes (pour ne pas avoir la tête dedans)
            // Mais les autres joueurs la verront !
            MeshRenderer renderer = GetComponentInChildren<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }
    }

    private void TrouverCamera()
    {
        Camera cameraActive = Camera.main;
        if (cameraActive != null)
        {
            cameraTransform = cameraActive.transform;
            Debug.Log("Succès : Caméra trouvée pour le joueur réseau !");
        }
        else
        {
            Debug.LogError("Erreur : Aucune caméra avec le tag 'MainCamera' n'a été trouvée !");
        }
    }

    void Update()
    {
        // Si on a l'autorité et qu'on a trouvé la caméra, on la suit à chaque frame
        if (HasAuthority && cameraTransform != null)
        {
            // On copie la position et la rotation de la caméra du casque/téléphone
            transform.position = cameraTransform.position;
            transform.rotation = cameraTransform.rotation;
        }
    }
}