using UnityEngine;

/// <summary>
/// Cache le MeshRenderer de cet objet si le device est en mode AR.
/// Utilisé typiquement pour le sol (Plane) qui n'a pas de sens en AR
/// puisque le joueur AR voit le vrai sol physique à travers la caméra.
///
/// Setup : ajouter ce script sur le GameObject Plane (sol) dans la scène.
/// </summary>
public class VROnlyObject : MonoBehaviour
{
    private void Start()
    {
        if (!XRRigSwitcher.IsVRMode)
        {
            // En AR : cache le mesh (le vrai sol physique fait office de sol)
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.enabled = false;
                Debug.Log($"[VROnlyObject] '{gameObject.name}' masqué en mode AR.");
            }

            // Cache aussi le Collider pour éviter les interactions AR indésirables
            Collider col = GetComponent<Collider>();
            if (col != null)
                col.enabled = false;
        }
    }
}
