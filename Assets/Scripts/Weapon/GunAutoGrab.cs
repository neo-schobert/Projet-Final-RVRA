using System.Collections;
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
    [Tooltip("Délai en secondes avant de tenter le grab (laisse le temps au joueur de spawner)")]
    public float grabDelay = 0.8f;

    private void Start()
    {
        // Seulement en VR et si le gun était tenu
        if (!XRRigSwitcher.IsVRMode) return;
        if (!GunPersistence.WasHeld) return;

        StartCoroutine(AutoGrabCoroutine());
    }

    private IEnumerator AutoGrabCoroutine()
    {
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
            // Fallback : essaie l'autre main
            targetSide       = GunPersistence.WasRightHand ? "left" : "right";
            targetInteractor = FindHandInteractor(targetSide);
        }

        if (targetInteractor == null)
        {
            Debug.LogError("[GunAutoGrab] Aucun interactor trouvé, abandon.");
            yield break;
        }

        interactionManager.SelectEnter(targetInteractor, interactable as IXRSelectInteractable);
        Debug.Log($"[GunAutoGrab] Gun auto-grabé dans la main {targetSide}.");

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
            // Remonte la hiérarchie jusqu'à trouver le nom de la main
            Transform t = interactor.transform;
            while (t != null)
            {
                if (t.name.ToLower().Contains(side))
                    return interactor as IXRSelectInteractor;
                t = t.parent;
            }
        }
        return null;
    }
}
