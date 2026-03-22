using UnityEngine;

public class Bullet : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("VRPlayer"))
        {
            Debug.Log("Joueur touché !");
            Destroy(gameObject);
        }
        else {
            Debug.Log($"Bullet a touché {other.gameObject.name}");
            Destroy(gameObject);
        }
    }
}