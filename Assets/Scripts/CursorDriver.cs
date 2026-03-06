using UnityEngine;
using Unity.Netcode;

public class CursorDriver : NetworkBehaviour
{
    [SerializeField] private PlayerController playerController;
    [SerializeField] private Camera           firstPersonCam;
    [SerializeField] private Camera           thirdPersonCam;
    [SerializeField] private float defaultDepth = 5f;
    [SerializeField] private float minDepth     = 0.5f;
    [SerializeField] private float maxDepth     = 50f;
    [SerializeField] private float scrollSpeed  = 1f;

    [Header("Spawn")]
    [SerializeField] private GameObject objectToCreate;
    [SerializeField] private NetworkObject networkObjectToCreate;

    private bool   active;
    private float  depth;

    private Camera ActiveCam =>
        (firstPersonCam != null && firstPersonCam.enabled) ? firstPersonCam :
        (thirdPersonCam != null && thirdPersonCam.enabled) ? thirdPersonCam : null;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!HasAuthority) return;

        depth  = defaultDepth;
        active = false;

        if (playerController == null) Debug.LogError("[CursorDriver] PlayerController non assigné !");
        if (firstPersonCam   == null) Debug.LogError("[CursorDriver] firstPersonCam non assignée !");
        if (thirdPersonCam   == null) Debug.LogError("[CursorDriver] thirdPersonCam non assignée !");
        if (objectToCreate   == null) Debug.LogWarning("[CursorDriver] objectToCreate non assigné !");
    }

    void Update()
    {
        if (!HasAuthority || !IsSpawned) return;

        if (Input.GetKeyDown(KeyCode.LeftAlt))
        {
            active = true;
            playerController?.SetCursorDriverActive(true);
        }

        if (Input.GetKeyUp(KeyCode.LeftAlt))
        {
            active = false;
            playerController?.SetCursorDriverActive(false);
        }

        // Spawn sur P, même si le curseur n'est pas actif
        if (Input.GetKeyDown(KeyCode.P))
        {
            SpawnObject();
        }

        if (!active) return;

        Camera cam = ActiveCam;
        if (cam == null)
        {
            Debug.LogWarning("[CursorDriver] Aucune caméra active trouvée !");
            return;
        }

        depth += Input.mouseScrollDelta.y * scrollSpeed;
        depth  = Mathf.Clamp(depth, minDepth, maxDepth);

        Ray r = cam.ScreenPointToRay(Input.mousePosition);
        transform.position = r.GetPoint(depth);
    }

    private void SpawnObject()
    {
        if (networkObjectToCreate == null)
        {
            Debug.LogWarning("[CursorDriver] Aucun objet à spawner !");
            return;
        }
    
        Debug.Log("[CursorDriver] Spawn requested");
    
        var instance = Instantiate(networkObjectToCreate, transform.position, Quaternion.identity);
        instance.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);
    }
}