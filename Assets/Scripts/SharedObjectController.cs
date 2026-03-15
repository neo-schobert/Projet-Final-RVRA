using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;


/// <summary>
/// À mettre sur n'importe quel objet partagé (cube, rose, etc.)
///
/// VR → Branche XRGrabInteractable.selectEntered → OnGrabbed() dans l'Inspector
/// AR → drag avec le doigt (automatique)
///
/// ClientNetworkTransform (requis sur le même GameObject) synchronise la position.
/// IMPORTANT : utilise ClientNetworkTransform (pas NetworkTransform) sur le prefab !
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(ClientNetworkTransform))]
public class SharedObjectController : NetworkBehaviour
{
    // ── Appelé depuis l'Inspector (XRGrabInteractable.selectEntered → OnGrabbed) ──

    /// <summary>
    /// Branche ceci sur l'event "Select Entered" de ton XRGrabInteractable dans l'Inspector.
    /// </summary>
    public void OnGrabbed()
    {
        NetworkObject.RequestOwnership();
        Debug.Log("[SharedObject] Ownership demandé (grab VR).");
    }

    // ── AR : drag avec le doigt ────────────────────────────────────────────────

    private bool  _isDragging   = false;
    private float _dragDistance = 1f;

    private void Update()
    {
        // Le drag AR ne s'active que si on n'est pas en VR
        if (XRRigSwitcher.IsVRMode) return;

        HandleARTouch();
    }

    private void HandleARTouch()
    {
        if (Input.touchCount == 0)
        {
            _isDragging = false;
            return;
        }

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Began)
        {
            Ray ray = Camera.main.ScreenPointToRay(touch.position);
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject)
            {
                _isDragging   = true;
                _dragDistance = Vector3.Distance(Camera.main.transform.position, transform.position);

                // Demande l'ownership → en DA mode le transfert est quasi-instantané
                NetworkObject.RequestOwnership();
                Debug.Log("[SharedObject] Ownership demandé (touch AR).");
            }
        }
        else if (touch.phase == TouchPhase.Moved && _isDragging)
        {
            // On bouge dès qu'on est propriétaire (ownership transféré via RequestOwnership)
            if (!IsOwner) return;

            Ray ray = Camera.main.ScreenPointToRay(touch.position);
            transform.position = ray.GetPoint(_dragDistance);
        }
        else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
        {
            _isDragging = false;
        }
    }
}
