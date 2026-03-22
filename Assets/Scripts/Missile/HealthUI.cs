using UnityEngine;
using TMPro;
using Unity.Netcode;
using System.Collections;

public class HealthUI : NetworkBehaviour
{
    [Header("Texte vie")]
    public TextMeshPro healthText;
    public Vector3 offset = new Vector3(0.3f, -0.2f, 0.5f);

    [Header("Résultat")]
    public TextMeshProUGUI victoireText;
    public TextMeshPro defaiteText;

    private PlayerHealth _playerHealth;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
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
        if (healthText == null)
        {
            var go = new GameObject("HealthText");
            go.transform.SetParent(transform);
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one * 0.05f;
            healthText = go.AddComponent<TextMeshPro>();
            healthText.alignment = TextAlignmentOptions.BottomRight;
            healthText.fontSize  = 5f;
            healthText.color     = Color.white;
        }

        if (victoireText != null) victoireText.gameObject.SetActive(false);
        if (defaiteText  != null) defaiteText.gameObject.SetActive(false);

        _playerHealth.OnHealthChangedEvent += UpdateText;
        UpdateText(_playerHealth.GetHealth());
    }

    public override void OnNetworkDespawn()
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