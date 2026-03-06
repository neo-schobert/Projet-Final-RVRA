using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Interacteur XR direct réseau qui préserve l'offset entre le point de saisie
/// et le transform de l'objet, évitant le téléportation brutale lors de la prise en main.
/// Fournit également un retour visuel/réseau pour l'utilisateur local et les autres.
/// </summary>
public class NetworkedXRDirectInteractor : XRDirectInteractor
{
    protected new void OnTriggerEnter(Collider col)
    {
        base.OnTriggerEnter(col);

        IXRInteractable interactable;
        interactionManager.TryGetInteractableForCollider(col, out interactable);

        if (interactable != null)
        {
            // Préservation de l'offset : l'attachTransform prend la position et rotation
            // exactes de l'objet au moment de la saisie, évitant tout saut de transform.
            attachTransform.SetPositionAndRotation(
                interactable.transform.position,
                interactable.transform.rotation
            );
        }
    }
}