using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// À placer sur un GameObject qui possède un XRGrabInteractable.
/// Bloque le grab si l'interacteur est à plus de maxGrabDistance du centre de l'objet.
/// Permet uniquement les grabs en contact direct (DirectInteractor, main proche).
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class ProximityGrabFilter : MonoBehaviour, IXRSelectFilter
{
    [SerializeField, Tooltip("Distance max pour pouvoir attraper l'objet (en mètres)")]
    private float maxGrabDistance = 0.5f;

    public bool canProcess => isActiveAndEnabled;

    private XRGrabInteractable _interactable;

    private void Awake()
    {
        _interactable = GetComponent<XRGrabInteractable>();
    }

    private void OnEnable()
    {
        _interactable.selectFilters.Add(this);
    }

    private void OnDisable()
    {
        _interactable.selectFilters.Remove(this);
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        float distance = Vector3.Distance(
            interactor.GetAttachTransform(interactable).position,
            interactable.transform.position
        );
        return distance <= maxGrabDistance;
    }
}
