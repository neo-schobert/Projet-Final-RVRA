using UnityEngine;

public class Bullet : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("ARPlayer"))
        {
            Debug.Log("[Bullet] Joueur AR touché !");
            Destroy(gameObject);

            // Seul le client VR déclenche la fin de partie.
            // Les balles sont spawned sur TOUS les clients via SpawnBulletRpc,
            // donc on filtre avec IsVRMode pour éviter un double-trigger depuis l'AR.
            if (XRRigSwitcher.IsVRMode && TempleEndGame.Instance != null)
                TempleEndGame.Instance.DeclareVRWins();
        }
        else if (other.CompareTag("DestroyBullet"))
        {
            Destroy(gameObject);
        }
        else
        {
            Debug.Log($"[Bullet] Touché : {other.gameObject.name}");
        }
    }
}
