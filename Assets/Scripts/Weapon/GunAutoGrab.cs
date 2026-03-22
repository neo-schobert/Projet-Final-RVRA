using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// À mettre sur le gun dans TempleScene.
/// Si GunPersistence.WasHeld == true, force-grab le gun dans la bonne main
/// dès que le joueur VR est prêt.
/// </summary>
public class GunAutoGrab : MonoBehaviour
{
    [Tooltip("Délai en secondes avant de tenter le grab (laisse le temps au joueur et au réseau de spawner)")]
    public float grabDelay = 1.2f;

    private void Start()
    {
        // Seulement en VR et si le gun était tenu
        if (!XRRigSwitcher.IsVRMode) return;
        if (!GunPersistence.WasHeld)
        {
            Debug.Log("[GunAutoGrab] GunPersistence.WasHeld=false → pas de grab automatique.");
            return;
        }

        Debug.Log("[GunAutoGrab] WasHeld=true → démarrage AutoGrabCoroutine.");
        StartCoroutine(AutoGrabCoroutine());
    }

    private IEnumerator AutoGrabCoroutine()
    {
        // Attendre que le NetworkObject soit spawné si le gun est networké
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null)
        {
            float timeout = 5f;
            while (!netObj.IsSpawned && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (!netObj.IsSpawned)
            {
                Debug.LogError("[GunAutoGrab] NetworkObject non spawné après 5s, abandon.");
                yield break;
            }
            Debug.Log("[GunAutoGrab] NetworkObject spawné, attente du délai...");
        }

        yield return new WaitForSeconds(grabDelay);

        var interactionManager = FindFirstObjectByType<XRInteractionManager>();
        var interactable       = GetComponent<XRGrabInteractable>();

        if (interactionManager == null)
        {
            Debug.LogError("[GunAutoGrab] XRInteractionManager introuvable !");
            yield break;
        }
        if (interactable == null)
        {
            Debug.LogError("[GunAutoGrab] XRGrabInteractable introuvable sur le gun !");
            yield break;
        }

        // Cherche la main cible (droite ou gauche) en remontant la hiérarchie
        string targetSide = GunPersistence.WasRightHand ? "right" : "left";
        IXRSelectInteractor targetInteractor = FindHandInteractor(targetSide);

        if (targetInteractor == null)
        {
            Debug.LogWarning($"[GunAutoGrab] Interactor '{targetSide}' introuvable, tentative avec l'autre main...");
            targetSide       = GunPersistence.WasRightHand ? "left" : "right";
            targetInteractor = FindHandInteractor(targetSide);
        }

        if (targetInteractor == null)
        {
            Debug.LogError("[GunAutoGrab] Aucun interactor trouvé dans la scène, abandon.");
            yield break;
        }

        // ── IMPORTANT : téléporter le gun dans la main avant le grab ──────────
        // Le ProximityGrabFilter rejette les SelectEnter si l'interactable est
        // trop loin de l'interactor. On place le gun directement dans la main
        // pour que la distance soit 0 et que le filtre laisse passer.
        var handMono = targetInteractor as MonoBehaviour;
        if (handMono != null)
        {
            transform.position = handMono.transform.position;
            transform.rotation = handMono.transform.rotation;
            Debug.Log($"[GunAutoGrab] Gun téléporté à {handMono.transform.position:F2} (main {targetSide})");
        }

        // Une frame pour que la physique prenne en compte la nouvelle position
        yield return null;

        interactionManager.SelectEnter(targetInteractor, interactable as IXRSelectInteractable);
        Debug.Log($"[GunAutoGrab] SelectEnter déclenché → main {targetSide}.");

        // Réinitialise pour ne pas re-graber à la prochaine transition
        GunPersistence.Reset();
    }

    /// <summary>
    /// Parcourt tous les XRBaseInteractor de la scène et retourne celui
    /// dont la hiérarchie contient "left" ou "right".
    /// </summary>
    private IXRSelectInteractor FindHandInteractor(string side)
    {
        var allInteractors = FindObjectsByType<XRBaseInteractor>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var interactor in allInteractors)
        {
            Transform t = interactor.transform;
            while (t != null)
            {
                if (t.name.ToLower().Contains(side))
                    return interactor as IXRSelectInteractor;
                t = t.parent;
            }
        }

        Debug.LogWarning($"[GunAutoGrab] Aucun interactor contenant '{side}' dans la hiérarchie. " +
                         $"Interactors trouvés : {allInteractors.Length}");
        foreach (var i in allInteractors)
            Debug.Log($"  → {i.gameObject.name} (parent: {i.transform.parent?.name})");

        return null;
    }
}
