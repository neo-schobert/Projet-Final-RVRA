using UnityEngine;
using Unity.Netcode;

public class UniversalPlayerController : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        // On vérifie si ce personnage appartient à l'appareil local (le tien)
        if (HasAuthority)
        {
            AttacherALaCamera();
        }
    }

    private void AttacherALaCamera()
    {
        // On cherche la caméra active de la scène (casque VR ou téléphone AR)
        Camera cameraActive = Camera.main;

        if (cameraActive != null)
        {
            // On devient l'enfant de la caméra pour copier tous ses mouvements automatiquement !
            transform.SetParent(cameraActive.transform);
            
            // On se centre parfaitement sur la caméra
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            Debug.Log("Succès : Le joueur réseau s'est attaché à la caméra !");
        }
        else
        {
            Debug.LogError("Erreur : Aucune caméra avec le tag 'MainCamera' n'a été trouvée !");
        }
    }
}