using UnityEngine;
using Unity.Netcode;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Vie")]
    public float maxHealth = 100f;

    public event System.Action<float> OnHealthChangedEvent;

    private NetworkVariable<float> _currentHealth = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        _currentHealth.Value = maxHealth;

        _currentHealth.OnValueChanged += OnHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        _currentHealth.OnValueChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(float oldVal, float newVal)
    {
        OnHealthChangedEvent?.Invoke(newVal);
    }

    public void TakeDamage(float damage)
    {
        if (!IsServer) return;

        _currentHealth.Value = Mathf.Max(0f, _currentHealth.Value - damage);

        if (_currentHealth.Value <= 0f)
            Die();
    }

    private void Die()
    {
        Debug.Log($"{gameObject.name} est mort !");
    }

    public float GetHealth() => _currentHealth.Value;
}