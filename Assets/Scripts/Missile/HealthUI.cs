using UnityEngine;
using TMPro;
using System.Collections;
using Unity.Netcode;

// MonoBehaviour (pas NetworkBehaviour) : HealthUI ne fait aucun RPC ni NetworkVariable.
// Il lit simplement PlayerHealth.GetHealth() (déjà synchronisé par NGO).
// Utiliser NetworkBehaviour obligerait à avoir un NetworkObject bien configuré
// → OnNetworkSpawn() ne se déclenche jamais si le setup réseau est manquant → HP jamais affiché.
public class HealthUI : MonoBehaviour
{
    [Header("Texte vie")]
    public TextMeshPro healthText;


    [Header("Résultat")]
    public TextMeshProUGUI victoireText;
    public TextMeshPro defaiteText;

    private PlayerHealth _playerHealth;

    private void Start()
    {
        // Start() démarre toujours, sans dépendre d'un NetworkObject.
        StartCoroutine(WaitForPlayerHealth());
    }

    private IEnumerator WaitForPlayerHealth()
    {
        // Attend que le PlayerVR local soit spawné et enregistré
        while (_playerHealth == null)
        {
            // Cherche dans toute la scène le PlayerHealth qui nous appartient
            foreach (var ph in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                var nb = ph.GetComponent<NetworkBehaviour>();
                if (nb != null && nb.IsOwner)
                {
                    _playerHealth = ph;
                    break;
                }
            }

            if (_playerHealth == null)
                yield return null; // attend la frame suivante
        }

        SetupUI();
    }

    private void SetupUI()
    {

        if (victoireText != null) victoireText.gameObject.SetActive(false);
        if (defaiteText  != null) defaiteText.gameObject.SetActive(false);

        _playerHealth.OnHealthChangedEvent += UpdateText;
        UpdateText(_playerHealth.GetHealth());
    }

    private void OnDestroy()
    {
        if (_playerHealth != null)
            _playerHealth.OnHealthChangedEvent -= UpdateText;
    }


    private void UpdateText(float value)
    {
        if (healthText == null) return;

        if (value <= 0f)
        {
            healthText.gameObject.SetActive(false);
            if (defaiteText  != null) defaiteText.gameObject.SetActive(true);
            if (victoireText != null) victoireText.gameObject.SetActive(false);
        }
        else
        {
            healthText.gameObject.SetActive(true);
            healthText.text = $"HP : {Mathf.CeilToInt(value)}";
        }
    }

    public void ShowVictory()
    {
        if (healthText   != null) healthText.gameObject.SetActive(false);
        if (defaiteText  != null) defaiteText.gameObject.SetActive(false);
        if (victoireText != null) victoireText.gameObject.SetActive(true);
    }
}