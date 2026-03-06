using UnityEngine;

/// <summary>
/// ConnectionManager pour la VRScene.
/// Appelable depuis un interactable XR via JoinSession().
/// Désactive le XR Rig local une fois la SharedScene chargée
/// pour laisser le VRPlayer networké prendre le relais.
/// </summary>
public class VRConnectionManager : BaseConnectionManager
{
    [Header("VR")]
    [SerializeField] private string     _profileName = "VRPlayer";
    [SerializeField] private GameObject _localXRRig;  // XR Origin déjà dans la VRScene

    // Garde contre le spam du XRInteractionManager (fire sur plusieurs frames)
    private bool _joinRequested;

    // ─── API publique (appelable depuis un XR Interactable) ──────────────────

    /// <summary>Utilise les valeurs de l'Inspector.</summary>
    public void JoinSession()
    {
        if (_joinRequested) return;
        _joinRequested = true;
        _ = ConnectAsync(_profileName, _sessionName);
    }

    /// <summary>Surcharge avec profil et session custom.</summary>
    public void JoinSession(string profileName, string sessionName)
    {
        if (_joinRequested) return;
        _joinRequested = true;
        _ = ConnectAsync(profileName, sessionName);
    }

    // ─── Post-chargement SharedScene ─────────────────────────────────────────

    protected override void OnSharedSceneLoaded()
    {
        if (_localXRRig != null)
        {
            _localXRRig.SetActive(false);
            Debug.Log("[VRConnectionManager] XR Rig local désactivé.");
        }
        else
        {
            Debug.LogWarning("[VRConnectionManager] _localXRRig non assigné dans l'Inspector !");
        }
    }
}