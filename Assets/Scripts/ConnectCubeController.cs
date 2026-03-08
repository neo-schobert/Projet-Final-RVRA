using UnityEngine;

public class ConnectCubeController : MonoBehaviour
{
    public VRConnectionManager connectionManager;
    private bool aDejaClique = false;

    void Update()
    {
        // On lance le Raycast UNIQUEMENT si le joueur clique / appuie sur la gâchette
        // ("Fire1" correspond souvent à la gâchette VR par défaut, ou au clic gauche)
        if (Input.GetButtonDown("Fire1") || Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2));
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                // Si on touche le cube et qu'on n'a pas encore validé
                if (hit.collider.gameObject == gameObject && !aDejaClique)
                {
                    aDejaClique = true; // On bloque les clics suivants
                    connectionManager.JoinSharedWorld();
                }
            }
        }
    }
}