using Unity.XR.CoreUtils;
using UnityEngine;
using Unity.Netcode;

public class MissileLauncher : NetworkBehaviour
{
    [Header("Missile")]
    public GameObject missilePrefab;
    public float arcHeight = 50f;
    public float flightDuration = 3f;

    [Header("Explosion")]
    public float explosionRadius = 10f;
    public float explosionDamage = 100f;
    public GameObject explosionVFX;

    [Header("Raycast")]
    public LayerMask mapLayer;

    private Camera _arCamera;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        _arCamera = GetComponentInChildren<Camera>();
    }

    void Update()
    {
        if (!IsOwner) return;
        if (_arCamera == null) return;
        if (Input.touchCount == 0) return;

        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began) return;

        Ray ray = _arCamera.ScreenPointToRay(touch.position);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, mapLayer))
        {
            LaunchMissileServerRpc(_arCamera.transform.position, hit.point);
        }
    }

    [ServerRpc]
    private void LaunchMissileServerRpc(Vector3 startPos, Vector3 targetPos)
    {
        GameObject missile = Instantiate(missilePrefab, startPos, Quaternion.identity);
        var no = missile.GetComponent<NetworkObject>();
        no.Spawn();

        var mover = missile.GetComponent<MissileMovement>();
        mover.Launch(startPos, targetPos, arcHeight, flightDuration,
                     explosionRadius, explosionDamage, explosionVFX);
    }
}
