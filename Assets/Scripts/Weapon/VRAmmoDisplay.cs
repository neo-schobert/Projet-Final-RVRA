using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// À mettre sur le GameObject "Munitions" (TextMeshPro) dans la caméra VR.
///
/// Se connecte automatiquement au Shoot.ammoText du gun owned par ce client.
/// Pas besoin d'assigner quoi que ce soit dans l'Inspector.
/// </summary>
[RequireComponent(typeof(TextMeshPro))]
public class VRAmmoDisplay : MonoBehaviour
{
    private TextMeshPro _text;

    private void Start()
    {
        _text = GetComponent<TextMeshPro>();

        // Ne rien faire côté AR — le texte munitions n'a de sens que pour le VR
        if (!XRRigSwitcher.IsVRMode)
        {
            gameObject.SetActive(false);
            return;
        }

        StartCoroutine(ConnectToShoot());
    }

    private IEnumerator ConnectToShoot()
    {
        Shoot shoot = null;
        float timeout = 10f;

        // Attend que le Shoot owned par ce client soit spawné
        while (shoot == null && timeout > 0f)
        {
            foreach (var s in FindObjectsByType<Shoot>(FindObjectsSortMode.None))
            {
                var nb = s.GetComponent<NetworkBehaviour>();
                if (nb != null && nb.IsOwner)
                {
                    shoot = s;
                    break;
                }
            }

            if (shoot == null)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        if (shoot == null)
        {
            Debug.LogWarning("[VRAmmoDisplay] Shoot introuvable après 10s — munitions non affichées.");
            yield break;
        }

        // Lier ce TextMeshPro au champ ammoText de Shoot
        shoot.ammoText = _text;

        // Forcer un rafraîchissement immédiat (OnNetworkSpawn a pu tourner avant la liaison)
        shoot.RefreshAmmoDisplay();

        Debug.Log("[VRAmmoDisplay] ✓ Connecté à Shoot — munitions affichées.");
    }
}
