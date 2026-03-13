using Unity.Netcode;
using UnityEngine;

/// <summary>
/// À mettre sur les prefabs PlayerVR et PlayerAR.
///
/// Quand c'est TON joueur local (HasAuthority) :
///   → S'attache à Camera.main pour suivre ta tête automatiquement.
///
/// Quand c'est un joueur distant :
///   → Ne fait rien (le NetworkTransform sync la position depuis le réseau).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerSetup : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!HasAuthority) return;

        // Camera.main pointe vers la caméra active :
        //   - VR : la caméra dans le XR Origin activé par XRRigSwitcher
        //   - AR : la caméra dans l'AR Session Origin activé par XRRigSwitcher
        Camera cam = Camera.main;

        if (cam == null)
        {
            Debug.LogError("[PlayerSetup] Camera.main introuvable ! Vérifie que le bon rig XR est activé.");
            return;
        }

        // On devient enfant de la caméra → on suit tous ses mouvements (tête, rotation, etc.)
        transform.SetParent(cam.transform);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        Debug.Log($"[PlayerSetup] Joueur local attaché à la caméra : {cam.name}");
    }
}
