using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class MissileLauncher : NetworkBehaviour
{
    [Header("Missile")]
    public GameObject missilePrefab;
    public float arcHeight = 50f;
    public float flightDuration = 3f;

    [Tooltip("Nombre maximum de missiles non-explosés simultanément. " +
             "Le tir est bloqué tant que cette limite est atteinte.")]
    public int maxActiveMissiles = 2;

    [Header("Explosion")]
    public float explosionRadius = 10f;
    public float explosionDamage = 100f;
    // explosionVFX retiré d'ici : il est désormais assigné directement sur le prefab
    // missile (champ sérialisé MissileMovement._explosionVFX). Un GameObject ne peut
    // pas être envoyé via RPC réseau → la référence doit vivre sur le prefab spawné.

    [Header("Raycast")]
    public LayerMask mapLayer;

    private Camera _arCamera;

    // Anti double-tir : timestamp du dernier tir. Ignore tout nouveau tir
    // dans les 0.4 s suivants (corrige le bug "2 missiles par tap" sur Android
    // dû à TouchPhase.Began pouvant être reporté sur 2 frames consécutives).
    private float _lastFireTime = -10f;
    private const float FireCooldown = 0.4f;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        // FIX #1 : La caméra AR vit dans le XR Origin, pas dans le prefab joueur.
        // On utilise Camera.main, identique au pattern de PlayerSetup.
        _arCamera = Camera.main;
    }

    void Update()
    {
        if (!IsOwner) return;

        // FIX #1 (suite) : réessaye chaque frame si la caméra AR Foundation
        // n'était pas encore initialisée au moment du spawn.
        if (_arCamera == null)
        {
            _arCamera = Camera.main;
            if (_arCamera == null) return;
        }

        if (Time.time - _lastFireTime < FireCooldown) return;

        if (!ARProximityCalibration.IsGameStarted) return;

        // ── Détection de l'input selon la plateforme ─────────────────────────
        // UNITY_EDITOR : clic gauche souris  → simule un tap sur l'écran AR.
        // Appareil AR   : premier touch Began → comportement de production.
        bool shouldFire  = false;
        Vector2 screenPos = Vector2.zero;

#if UNITY_EDITOR
        // Éditeur — New Input System : Mouse.current remplace l'ancien Input.GetMouseButtonDown.
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            shouldFire = true;
            screenPos  = Mouse.current.position.ReadValue();
            Debug.Log("[MissileLauncher][Editor] Clic gauche → simulation tap AR.");
        }
#else
        // Production (Android / AR Foundation) — New Input System : Touchscreen.current.
        var ts = Touchscreen.current;
        if (ts != null)
        {
            foreach (var touch in ts.touches)
            {
                if (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    shouldFire = true;
                    screenPos  = touch.position.ReadValue();
                    break;
                }
            }
        }
#endif

        if (!shouldFire) return;

        Ray ray = _arCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, mapLayer))
        {
            // FIX #2 : En Distributed Authority il n'y a pas de serveur dédié.
            // Le client propriétaire spawne directement son objet réseau.
            SpawnMissile(_arCamera.transform.position, hit.point);
        }
    }

    // FIX #2 : Spawn direct côté client DA — remplace LaunchMissileServerRpc.
    private void SpawnMissile(Vector3 startPos, Vector3 targetPos)
    {
        if (missilePrefab == null)
        {
            Debug.LogError("[MissileLauncher] missilePrefab non assigné dans l'Inspector !");
            return;
        }

        // ── Limite de missiles actifs ──────────────────────────────────────────
        // Compte les missiles en vol qui n'ont pas encore explosé.
        // Les missiles explosés (IsExploded=true) ne comptent pas même s'ils
        // sont encore dans la scène (ils attendent leur Despawn de 1.5 s).
        int actifs = 0;
        foreach (var m in FindObjectsByType<MissileMovement>(FindObjectsSortMode.None))
        {
            if (m.IsLaunched && !m.IsExploded)
                actifs++;
        }

        if (actifs >= maxActiveMissiles)
        {
            Debug.Log($"[MissileLauncher] Tir bloqué — {actifs}/{maxActiveMissiles} " +
                      "missiles actifs en vol.");
            return;
        }
        GameObject missile = Instantiate(missilePrefab, startPos, Quaternion.identity);
        var no = missile.GetComponent<NetworkObject>();
        no.Spawn();

        // LaunchRpc (SendTo.Everyone) : envoie les paramètres de vol à TOUS les clients
        // (AR + VR). Sans ça, le VR reçoit le NetworkObject spawné mais _launched reste
        // false → le missile ne bouge pas côté VR (invisible en pratique).
        var mover = missile.GetComponent<MissileMovement>();
        mover.LaunchRpc(startPos, targetPos, arcHeight, flightDuration,
                        explosionRadius, explosionDamage);
    }
}
