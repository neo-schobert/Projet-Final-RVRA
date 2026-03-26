using UnityEngine;
using Unity.Netcode;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Vie")]
    public float maxHealth = 100f;

    [Header("Audio")]
    [Tooltip("AudioSource avec le clip de dégâts déjà assigné dessus. Le son joue uniquement sur l'owner (VR).")]
    [SerializeField] private AudioSource _audioSource;

    public event System.Action<float> OnHealthChangedEvent;

    // FIX : En DA il n'y a pas de serveur. Le joueur est propriétaire de sa propre santé.
    private NetworkVariable<float> _currentHealth = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public override void OnNetworkSpawn()
    {
        // Souscrire D'ABORD, PUIS écrire la valeur initiale.
        // Raison : en NGO, NetworkVariable.OnValueChanged se déclenche LOCALEMENT
        // dès que l'owner écrit. Si on souscrit après, l'owner rate l'événement initial.
        _currentHealth.OnValueChanged += OnHealthChanged;

        if (IsOwner)
        {
            _currentHealth.Value = maxHealth;  // déclenche OnHealthChanged correctement
            Debug.Log($"[PlayerHealth] {gameObject.name} initialisé à {maxHealth} PV.");
        }
    }

    public override void OnNetworkDespawn()
    {
        _currentHealth.OnValueChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(float oldVal, float newVal)
    {
        OnHealthChangedEvent?.Invoke(newVal);
    }

    // TakeDamage peut être appelé par n'importe quel client (ex : AR depuis l'explosion).
    // On envoie un RPC à l'OWNER du joueur pour qu'il applique les dégâts sur sa NetworkVariable.
    //
    // OBLIGATOIRE : InvokePermission.Everyone
    // Sans ça, en DA le RPC envoyé par un NON-owner est silencieusement ignoré par NGO.
    // (même bug que SyncCalibrationRpc dans PlayerSetup — la règle est identique)
    public void TakeDamage(float damage)
    {
        ApplyDamageRpc(damage);
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Everyone)]
    private void ApplyDamageRpc(float damage)
    {
        _currentHealth.Value = Mathf.Max(0f, _currentHealth.Value - damage);

        // Son de dégâts — joue uniquement sur le device owner (VR).
        // L'AudioListener est sur la caméra VR → le son est entendu en 2D.
        if (_audioSource != null && _audioSource.clip != null)
            _audioSource.PlayOneShot(_audioSource.clip);

        if (_currentHealth.Value <= 0f)
            Die();
    }

    private void Die()
    {
        Debug.Log($"{gameObject.name} est mort !");
    }

    public float GetHealth() => _currentHealth.Value;
}