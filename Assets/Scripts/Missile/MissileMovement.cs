using UnityEngine;
using Unity.Netcode;

public class MissileMovement : NetworkBehaviour
{
    private Vector3 _start;
    private Vector3 _target;
    private float _arcHeight;
    private float _duration;
    private float _elapsed;
    private float _explosionRadius;
    private float _explosionDamage;
    private GameObject _explosionVFX;
    private bool _launched;

    public void Launch(Vector3 start, Vector3 target, float arcHeight, float duration,
                       float explosionRadius, float explosionDamage, GameObject explosionVFX)
    {
        _start           = start;
        _target          = target;
        _arcHeight       = arcHeight;
        _duration        = duration;
        _explosionRadius = explosionRadius;
        _explosionDamage = explosionDamage;
        _explosionVFX    = explosionVFX;
        _elapsed         = 0f;
        _launched        = true;
    }

    void Update()
    {
        if (!_launched) return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);

        transform.position = Parabola(_start, _target, _arcHeight, t);

        if (t < 1f)
        {
            Vector3 nextPos = Parabola(_start, _target, _arcHeight, Mathf.Clamp01(t + 0.01f));
            transform.forward = (nextPos - transform.position).normalized;
        }

        if (t >= 1f)
        {
            if (IsServer) Explode();
        }
    }

    private void Explode()
    {
        // Spawn le VFX sur le serveur et le synchronise sur tous les clients
        if (_explosionVFX != null)
        {
            GameObject vfx = Instantiate(_explosionVFX, transform.position, Quaternion.identity);
            var no = vfx.GetComponent<NetworkObject>();
            if (no != null)
            {
                no.Spawn();
                // Le Particle System se détruit tout seul grâce à Stop Action = Destroy
                // mais on sync la destruction via le réseau
                StartCoroutine(DespawnVFXAfterDelay(no, 3f));
            }
        }

        // Dégâts dans le rayon
        Collider[] hits = Physics.OverlapSphere(transform.position, _explosionRadius);
        foreach (var hit in hits)
        {
            float distance = Vector3.Distance(transform.position, hit.transform.position);
            float falloff  = 1f - Mathf.Clamp01(distance / _explosionRadius);
            float damage   = _explosionDamage * falloff;

            var health = hit.GetComponent<PlayerHealth>();
            if (health != null)
                health.TakeDamage(damage);
        }

        GetComponent<NetworkObject>().Despawn();
    }

    private System.Collections.IEnumerator DespawnVFXAfterDelay(NetworkObject no, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (no != null && no.IsSpawned)
            no.Despawn();
    }

    private Vector3 Parabola(Vector3 start, Vector3 end, float height, float t)
    {
        Vector3 mid = (start + end) / 2f + Vector3.up * height;
        Vector3 a   = Vector3.Lerp(start, mid, t);
        Vector3 b   = Vector3.Lerp(mid,   end, t);
        return Vector3.Lerp(a, b, t);
    }
}