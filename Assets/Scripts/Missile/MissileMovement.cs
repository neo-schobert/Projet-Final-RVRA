using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class MissileMovement : NetworkBehaviour
{
    // ── VFX ──────────────────────────────────────────────────────────────────
    // VFX d'explosion assigné directement sur le prefab missile dans l'Inspector.
    // On ne peut pas passer un GameObject via RPC réseau → la référence doit
    // être présente localement sur chaque client via le prefab spawné.
    [Header("VFX")]
    [SerializeField] private GameObject _explosionVFX;

    // Hauteur au-dessus du point d'impact pour spawner l'explosion.
    // POURQUOI : le hit.point AR est exactement à Y=0 (surface du sol).
    // En VR, le mesh du sol écrit dans le depth buffer → les soft particles
    // (_SoftParticlesEnabled=1, FarFade=0.5u) faderaient toutes les particules
    // à 100% car elles sont au ras du sol → explosion invisible côté VR.
    // En AR, AR Foundation ne remplit pas le depth buffer pour le fond caméra
    // → les soft particles sont sans effet → explosion visible côté AR.
    // Offset de 1.5 u garantit que les particules sont au-dessus du sol.
    [SerializeField] private float _explosionHeightOffset = 1.5f;

    // ── Audio : explosion ─────────────────────────────────────────────────────
    // Son joué en 2D (spatialBlend=0) avec volume calculé manuellement selon
    // la distance joueur ↔ point d'impact au moment de l'explosion.
    [Header("Audio - Explosion")]
    [SerializeField] private AudioClip _explosionClip;
    [SerializeField] [Range(0f, 1f)] private float _explosionVolume    = 1f;
    [SerializeField] private float                 _explosionMinDistance = 1f;
    [SerializeField] private float                 _explosionMaxDistance = 30f;

    // ── Audio : vol du missile ────────────────────────────────────────────────
    // Son joué en 2D dès le lancement, volume mis à jour chaque frame selon
    // la distance joueur ↔ missile (qui change pendant le vol).
    // On garde une référence à l'AudioSource pour pouvoir le stopper à l'explosion
    // et mettre à jour le volume en temps réel dans Update().
    [Header("Audio - Vol")]
    [SerializeField] private AudioClip             _flightClip;
    [SerializeField] [Range(0f, 1f)] private float _flightVolume       = 1f;
    [SerializeField] private float                 _flightMinDistance   = 1f;
    [SerializeField] private float                 _flightMaxDistance   = 40f;

    // Référence gardée pour mise à jour volume en Update() + stop à l'explosion.
    private AudioSource _flightAudioSource;
    private GameObject  _flightAudioObj;

    // ── Paramètres de vol ─────────────────────────────────────────────────────
    private Vector3 _start;
    private Vector3 _target;
    private float   _arcHeight;
    private float   _duration;
    private float   _elapsed;
    private float   _explosionRadius;
    private float   _explosionDamage;
    private bool    _launched;
    private bool    _exploded; // garde : TriggerExplosionEffects() ne s'appelle qu'une fois

    // ── RPC broadcast ─────────────────────────────────────────────────────────
    // SendTo.Everyone garantit que VR ET AR reçoivent les paramètres de vol
    // et démarrent l'animation du missile. Sans ça, _launched reste false
    // sur le VR → le missile spawné est invisible (immobile à la position initiale).
    [Rpc(SendTo.Everyone)]
    public void LaunchRpc(Vector3 start, Vector3 target, float arcHeight, float duration,
                          float explosionRadius, float explosionDamage)
    {
        SetupAndLaunch(start, target, arcHeight, duration, explosionRadius, explosionDamage);
    }

    private void SetupAndLaunch(Vector3 start, Vector3 target, float arcHeight, float duration,
                                float explosionRadius, float explosionDamage)
    {
        _start           = start;
        _target          = target;
        _arcHeight       = arcHeight;
        _duration        = duration;
        _explosionRadius = explosionRadius;
        _explosionDamage = explosionDamage;
        _elapsed         = 0f;
        _launched        = true;
        _exploded        = false;

        // Démarre le son de vol dès le lancement.
        // Le volume sera mis à jour chaque frame dans Update() selon la distance.
        StartFlightAudio();
    }

    // ── Update : vol parabolique + volume de vol + déclenchement explosion ───
    void Update()
    {
        if (!_launched) return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);

        transform.position = Parabola(_start, _target, _arcHeight, t);

        if (t < 1f)
        {
            Vector3 nextPos = Parabola(_start, _target, _arcHeight, Mathf.Clamp01(t + 0.01f));
            Vector3 dir     = (nextPos - transform.position).normalized;
            if (dir.sqrMagnitude > 0.001f)
            {
                // Le prefab missile pointe vers +Y (axe up).
                // FromToRotation(up, dir) aligne l'axe Y sur la direction de vol
                // au lieu de transform.forward qui alignerait Z → mauvaise orientation.
                transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            }

            // Mise à jour du volume de vol chaque frame : le missile se déplace,
            // la distance joueur ↔ missile change → le volume doit suivre.
            UpdateFlightVolume();
        }

        if (t >= 1f && !_exploded)
        {
            _exploded = true;

            // Stoppe et détruit le son de vol avant de jouer l'explosion.
            StopFlightAudio();
            TriggerExplosionEffects();

            // Dégâts + Despawn : owner (AR) uniquement
            if (IsOwner)
                StartCoroutine(DespawnApresExplosion());
        }
    }

    // ── Son de vol ────────────────────────────────────────────────────────────

    private void StartFlightAudio()
    {
        if (_flightClip == null)
        {
            Debug.LogWarning("[MissileMovement] _flightClip est NULL — " +
                             "assigne un AudioClip de vol sur le MissilePrefab dans l'Inspector !");
            return;
        }

        _flightAudioObj                  = new GameObject("MissileFlightAudio");
        _flightAudioObj.transform.position = transform.position;

        _flightAudioSource               = _flightAudioObj.AddComponent<AudioSource>();
        _flightAudioSource.clip          = _flightClip;
        _flightAudioSource.spatialBlend  = 0f;   // 2D : volume géré manuellement
        _flightAudioSource.loop          = false; // le clip dure exactement _duration
        _flightAudioSource.playOnAwake   = false;
        _flightAudioSource.volume        = ComputeDistanceVolume(
            transform.position, _flightMinDistance, _flightMaxDistance, _flightVolume
        );
        _flightAudioSource.Play();
    }

    private void UpdateFlightVolume()
    {
        if (_flightAudioSource == null) return;

        // Suit la position du missile pour que la distance soit toujours correcte.
        _flightAudioObj.transform.position = transform.position;

        _flightAudioSource.volume = ComputeDistanceVolume(
            transform.position, _flightMinDistance, _flightMaxDistance, _flightVolume
        );
    }

    private void StopFlightAudio()
    {
        if (_flightAudioSource != null)
            _flightAudioSource.Stop();

        if (_flightAudioObj != null)
            Destroy(_flightAudioObj);

        _flightAudioSource = null;
        _flightAudioObj    = null;
    }

    // ── VFX + Audio explosion : chaque client instancie localement ────────────
    // On ne passe PAS par un RPC depuis ce NetworkObject.
    // Raison : en DA, le message Despawn peut arriver sur VR AVANT le RPC VFX
    // → RPC silencieusement ignoré par NGO (objet déjà détruit) → pas d'explosion VR.
    // Fix : Update() tourne sur AR ET VR (LaunchRpc a fourni les params aux deux).
    // Chaque client sait quand t>=1f et instancie les effets lui-même → garanti visible.
    private void TriggerExplosionEffects()
    {
        // +_explosionHeightOffset : spawn au-dessus du sol pour éviter que
        // les soft particles soient faded par le depth buffer du mesh du sol en VR.
        Vector3 explosionPos = transform.position + Vector3.up * _explosionHeightOffset;

        // ── VFX ──────────────────────────────────────────────────────────────
        if (_explosionVFX != null)
        {
            GameObject explosionInstance = Instantiate(_explosionVFX, explosionPos, Quaternion.identity);

            // Désactive les soft particles sur toutes les particules de l'explosion.
            // Double sécurité : même si l'offset est insuffisant, les particules
            // ne disparaîtront plus près de la géométrie en VR.
            foreach (var psr in explosionInstance.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (psr.material != null)
                {
                    // .material crée une instance locale → ne modifie pas l'asset d'origine
                    psr.material.SetFloat("_SoftParticlesEnabled", 0f);
                    psr.material.DisableKeyword("_SOFTPARTICLES_ON");
                }
            }

            Debug.Log($"[MissileMovement] ✓ Explosion VFX instancié à {explosionPos:F2} " +
                      $"(impact={transform.position:F2}, IsOwner={IsOwner})");
        }
        else
        {
            Debug.LogError("[MissileMovement] _explosionVFX est NULL — " +
                           "assigne le prefab explosion sur le MissilePrefab dans l'Inspector !");
        }

        // ── Audio explosion ───────────────────────────────────────────────────
        if (_explosionClip != null)
        {
            GameObject audioObj         = new GameObject("ExplosionAudio");
            audioObj.transform.position = explosionPos;

            AudioSource src  = audioObj.AddComponent<AudioSource>();
            src.clip         = _explosionClip;
            src.spatialBlend = 0f; // 2D : volume géré manuellement
            src.volume       = ComputeDistanceVolume(
                explosionPos, _explosionMinDistance, _explosionMaxDistance, _explosionVolume
            );
            src.playOnAwake  = false;
            src.Play();

            Destroy(audioObj, _explosionClip.length + 0.1f);

            Debug.Log($"[MissileMovement] ✓ Audio explosion joué à {explosionPos:F2} " +
                      $"volume={src.volume:F2} (IsOwner={IsOwner})");
        }
        else
        {
            Debug.LogWarning("[MissileMovement] _explosionClip est NULL — " +
                             "assigne un AudioClip d'explosion sur le MissilePrefab dans l'Inspector !");
        }
    }

    // ── Utilitaire : volume selon distance (courbe SmoothStep) ───────────────
    // SmoothStep donne une courbe en S plus naturelle à l'oreille qu'un lerp brut :
    // montée douce au début + descente douce à la fin → pas de coupure abrupte.
    // En dessous de minDistance → volume plein. Au-delà de maxDistance → silence.
    private float ComputeDistanceVolume(Vector3 position, float minDistance,
                                        float maxDistance, float maxVolume)
    {
        Camera localCam        = Camera.main;
        float distanceToPlayer = localCam != null
            ? Vector3.Distance(position, localCam.transform.position)
            : 0f;

        float t = Mathf.Clamp01(
            (distanceToPlayer - minDistance) / (maxDistance - minDistance)
        );
        return maxVolume * (1f - Mathf.SmoothStep(0f, 1f, t));
    }

    // ── Dégâts + Despawn : owner (AR) uniquement ─────────────────────────────
    private IEnumerator DespawnApresExplosion()
    {
        // Dégâts en zone
        Collider[] hits = Physics.OverlapSphere(transform.position, _explosionRadius);
        foreach (var hit in hits)
        {
            // GetComponentInParent : PlayerHealth est sur le ROOT du prefab joueur,
            // pas forcément sur le GameObject qui porte le collider (peut être un enfant).
            // GetComponent() seul retournerait null → 0 dégâts même si le joueur est touché.
            var health = hit.GetComponentInParent<PlayerHealth>();
            if (health == null) continue;

            float distance = Vector3.Distance(transform.position, hit.transform.position);
            float falloff  = 1f - Mathf.Clamp01(distance / _explosionRadius);
            float damage   = _explosionDamage * falloff;

            Debug.Log($"[MissileMovement] Dégâts : {hit.gameObject.name} → {damage:F1} dmg " +
                      $"(distance={distance:F1}m, falloff={falloff:F2})");
            health.TakeDamage(damage);
        }

        // Délai 1.5 s : laisse le temps au VR de recevoir t>=1f et d'instancier les effets
        // AVANT que le NetworkObject soit détruit par le Despawn.
        // Augmenté de 0.5→1.5 s : si latence réseau AR→VR > 0.5 s (WiFi dégradé),
        // VR n'avait pas le temps d'atteindre t>=1f avant le Despawn → pas d'explosion VR.
        yield return new WaitForSeconds(1.5f);

        if (IsSpawned)
            GetComponent<NetworkObject>().Despawn();
    }

    // ── Utilitaire : trajectoire parabolique ──────────────────────────────────
    private Vector3 Parabola(Vector3 start, Vector3 end, float height, float t)
    {
        Vector3 mid = (start + end) / 2f + Vector3.up * height;
        Vector3 a   = Vector3.Lerp(start, mid, t);
        Vector3 b   = Vector3.Lerp(mid,   end, t);
        return Vector3.Lerp(a, b, t);
    }
}