using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Prefab racine unique assigné aux deux NetworkManagers (Desktop et VR).
/// Active les composants Desktop ou VR selon la plateforme du joueur local.
/// Les autres joueurs voient uniquement le mesh/avatar approprié.
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    [SerializeField] private GameObject _desktopPlayer;
    [SerializeField] private GameObject _vrPlayer;

    // Synchronise le type de joueur pour que les autres clients
    // sachent quel avatar afficher
    private NetworkVariable<bool> _isVR = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            bool isVR = UnityEngine.XR.XRSettings.isDeviceActive;
            _isVR.Value = isVR;

            // Active uniquement les composants de notre plateforme
            _desktopPlayer.SetActive(!isVR);
            _vrPlayer.SetActive(isVR);

            Debug.Log($"[NetworkPlayer] Local — mode {(isVR ? "VR" : "Desktop")}");
        }
        else
        {
            // Pour les autres joueurs on attend la valeur réseau
            // puis on active le bon avatar
            _isVR.OnValueChanged += OnRemotePlayerTypeChanged;
            ApplyRemoteAvatar(_isVR.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isVR.OnValueChanged -= OnRemotePlayerTypeChanged;
    }

    private void OnRemotePlayerTypeChanged(bool oldValue, bool newValue)
        => ApplyRemoteAvatar(newValue);

    /// <summary>
    /// Pour les joueurs distants : active le bon avatar et désactive
    /// tous les composants de contrôle (camera, audio, XR, etc.)
    /// </summary>
    private void ApplyRemoteAvatar(bool isVR)
    {
        _desktopPlayer.SetActive(!isVR);
        _vrPlayer.SetActive(isVR);

        // Désactive caméra et audio du joueur distant pour ne pas
        // interférer avec notre propre vue
        DisableLocalComponents(_desktopPlayer);
        DisableLocalComponents(_vrPlayer);

        Debug.Log($"[NetworkPlayer] Remote — mode {(isVR ? "VR" : "Desktop")}");
    }

    /// <summary>
    /// Désactive les composants qui ne doivent exister que sur le joueur local.
    /// </summary>
    private void DisableLocalComponents(GameObject root)
    {
        if (root == null) return;

        foreach (var cam in root.GetComponentsInChildren<Camera>())
            cam.enabled = false;

        foreach (var audio in root.GetComponentsInChildren<AudioListener>())
            audio.enabled = false;
    }
}