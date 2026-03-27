using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Intercepte et détruit tout missile qui s'approche à moins de <see cref="_detectionRadius"/> mètres.
/// À mettre sur n'importe quel GameObject (statique ou NetworkObject).
///
/// FONCTIONNEMENT :
///   • Toutes les <see cref="_checkInterval"/> secondes, cherche les missiles actifs en vol.
///   • Si un missile entre dans le rayon → ForceExplode() + destruction de cet objet.
///
/// POURQUOI pas OnTriggerEnter :
///   MissileMovement déplace le missile via transform.position (pas de Rigidbody).
///   Unity ne déclenche OnTriggerEnter que pour les objets avec Rigidbody → la détection
///   par distance est la seule méthode fiable sans modifier le prefab missile.
///
/// DESTRUCTION EN RÉSEAU :
///   • Pas de NetworkObject sur cet objet → Destroy() sur chaque client (chaque client
///     détecte l'interception indépendamment via son propre Update).
///   • NetworkObject présent + IsOwner → Despawn() (synchronisé sur tous les clients).
///   • NetworkObject présent + NOT IsOwner → la destruction vient du Despawn de l'owner.
///
/// SETUP Inspector :
///   • <see cref="_detectionRadius"/> : rayon en mètres. Visualisé dans la scène (Gizmo).
///   • <see cref="_checkInterval"/>   : fréquence de vérification (0.05 s = 20 fois/s).
/// </summary>
public class MissileInterceptor : MonoBehaviour
{
    [Header("Détection")]
    [Tooltip("Rayon (m) dans lequel un missile déclenche l'interception. " +
             "Visualisé par une sphère jaune dans la vue Scène.")]
    [SerializeField] private float _detectionRadius = 1.5f;

    [Tooltip("Intervalle entre deux vérifications (secondes). " +
             "0.05 = 20 fois/s. Augmente si tu as beaucoup d'intercepteurs.")]
    [SerializeField] [Range(0.016f, 0.5f)] private float _checkInterval = 0.05f;

    // ── État interne ───────────────────────────────────────────────────────────
    private float _timer;
    private bool  _intercepted; // empêche les appels multiples si plusieurs missiles proches

    // ─────────────────────────────────────────────────────────────────────────
    //  UPDATE — détection par distance
    // ─────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_intercepted) return;

        _timer += Time.deltaTime;
        if (_timer < _checkInterval) return;
        _timer = 0f;

        // Récupère tous les missiles actifs dans la scène.
        // FindObjectsByType est O(n) sur les objets en scène — acceptable pour
        // quelques missiles simultanés.
        MissileMovement[] missiles =
            FindObjectsByType<MissileMovement>(FindObjectsSortMode.None);

        foreach (MissileMovement missile in missiles)
        {
            // Ignore les missiles pas encore lancés (LaunchRpc pas reçu)
            if (!missile.IsLaunched) continue;

            float dist = Vector3.Distance(transform.position, missile.transform.position);
            if (dist > _detectionRadius) continue;

            // ── INTERCEPTION ──────────────────────────────────────────────────
            Debug.Log($"[MissileInterceptor] ✓ Missile '{missile.name}' intercepté " +
                      $"à {dist:F2} m de '{gameObject.name}'.");

            _intercepted = true;

            // 1. Faire exploser le missile (VFX + audio + dégâts + Despawn par l'owner)
            missile.ForceExplode();

            // 2. Détruire cet objet
            DetruireCetObjet();

            return; // un seul missile par frame suffit
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DESTRUCTION
    // ─────────────────────────────────────────────────────────────────────────

    private void DetruireCetObjet()
    {
        // Si cet objet est un NetworkObject (spawné via NGO) :
        //   → l'owner le Despawn (synchronisé sur tous les clients).
        //   → le non-owner attend le message Despawn → ne rien faire ici.
        // Sinon (objet statique de scène) :
        //   → chaque client détecte l'interception dans son propre Update()
        //     et détruit localement → résultat cohérent sur tous les clients.
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            if (netObj.IsOwner)
            {
                Debug.Log($"[MissileInterceptor] NetworkObject Despawn → '{gameObject.name}'.");
                netObj.Despawn();
            }
            // Sinon : le Despawn de l'owner va arriver et détruire cet objet
        }
        else
        {
            // GameObject classique : Destroy sur ce client.
            // Update() tourne sur tous les clients → Destroy sera appelé sur chacun
            // → l'objet disparaît partout de façon cohérente.
            Debug.Log($"[MissileInterceptor] Destroy local → '{gameObject.name}'.");
            Destroy(gameObject);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GIZMO — visualisation du rayon dans la vue Scène
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.3f); // jaune semi-transparent
        Gizmos.DrawSphere(transform.position, _detectionRadius);

        Gizmos.color = new Color(1f, 0.8f, 0f, 1f); // contour plein
        Gizmos.DrawWireSphere(transform.position, _detectionRadius);
    }
}
