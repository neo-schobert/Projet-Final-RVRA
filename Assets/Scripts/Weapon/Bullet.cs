using UnityEngine;

public class Bullet : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("ARPlayer"))
        {
            Debug.Log("Joueur touché !");
            Destroy(gameObject);
        }
        else if (other.CompareTag("DestroyBullet"))
        {
            Destroy(gameObject);
        }
        else {
            Debug.Log($"Bullet a touché {other.gameObject.name}");
        }
    }
}