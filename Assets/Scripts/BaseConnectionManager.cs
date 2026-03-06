using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Classe de base partagée entre ConnectionManager (Desktop) et VRConnectionManager (VR).
/// Gère l'initialisation Unity Services, la connexion/déconnexion de session,
/// et le chargement additif de la SharedScene via le NetworkSceneManager.
/// </summary>
public abstract class BaseConnectionManager : MonoBehaviour
{
    [Header("Session")]
    [SerializeField] protected string _sessionName  = "DefaultSession";
    [SerializeField] protected int    _maxPlayers   = 10;

    [Header("Scene")]
    [SerializeField] private string _sharedSceneName = "SharedScene";

    protected ConnectionState _state = ConnectionState.Disconnected;
    protected ISession        _session;
    protected NetworkManager  m_NetworkManager;

    protected enum ConnectionState { Disconnected, Connecting, Connected }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    protected virtual async void Awake()
    {
        m_NetworkManager = GetComponent<NetworkManager>();
        m_NetworkManager.OnClientConnectedCallback += OnClientConnectedCallback;
        m_NetworkManager.OnSessionOwnerPromoted    += OnSessionOwnerPromoted;
        await UnityServices.InitializeAsync();
    }

    private void OnSessionOwnerPromoted(ulong sessionOwnerPromoted)
    {
        if (m_NetworkManager.LocalClient.IsSessionOwner)
            Debug.Log($"[{GetType().Name}] Client-{m_NetworkManager.LocalClientId} est le session owner.");
    }

    private void OnClientConnectedCallback(ulong clientId)
    {
        if (m_NetworkManager.LocalClientId == clientId)
            Debug.Log($"[{GetType().Name}] Client-{clientId} connecté.");
    }

    // ─── Connexion ────────────────────────────────────────────────────────────

    protected async Task ConnectAsync(string profileName, string sessionName)
    {
        if (_state != ConnectionState.Disconnected)
        {
            Debug.LogWarning($"[{GetType().Name}] Déjà connecté ou connexion en cours.");
            return;
        }

        _state = ConnectionState.Connecting;
        Debug.Log($"[{GetType().Name}] Connexion — profil: {profileName}, session: {sessionName}");

        try
        {
            // Si déjà authentifié (ex: tentative précédente ratée), on se déconnecte proprement
            if (AuthenticationService.Instance.IsSignedIn)
            {
                AuthenticationService.Instance.SignOut();
                Debug.Log($"[{GetType().Name}] Sign out préalable effectué.");
            }

            AuthenticationService.Instance.SwitchProfile(profileName);
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

            var options = new SessionOptions()
            {
                Name       = sessionName,
                MaxPlayers = _maxPlayers
            }.WithDistributedAuthorityNetwork();

            _session = await MultiplayerService.Instance.CreateOrJoinSessionAsync(sessionName, options);

            _state = ConnectionState.Connected;
            Debug.Log($"[{GetType().Name}] Connecté à la session: {sessionName}");

            LoadSharedScene();
        }
        catch (Exception e)
        {
            _state = ConnectionState.Disconnected;
            Debug.LogException(e);
        }
    }

    // ─── Chargement SharedScene via NetworkSceneManager ──────────────────────

    private void LoadSharedScene()
    {
        if (string.IsNullOrEmpty(_sharedSceneName)) return;

        if (SceneManager.GetSceneByName(_sharedSceneName).isLoaded)
        {
            Debug.Log($"[{GetType().Name}] '{_sharedSceneName}' déjà chargée.");
            OnSharedSceneLoaded();
            return;
        }

        // Netcode synchronise la scène sur tous les clients automatiquement
        m_NetworkManager.SceneManager.OnLoadComplete += OnNetworkSceneLoadComplete;
        m_NetworkManager.SceneManager.LoadScene(_sharedSceneName, LoadSceneMode.Additive);
    }

    private void OnNetworkSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (sceneName != _sharedSceneName) return;
        if (clientId  != m_NetworkManager.LocalClientId) return;

        m_NetworkManager.SceneManager.OnLoadComplete -= OnNetworkSceneLoadComplete;

        Debug.Log($"[{GetType().Name}] '{_sharedSceneName}' chargée via NetworkSceneManager.");
        OnSharedSceneLoaded();
    }

    /// <summary>
    /// Appelé juste après le chargement de la SharedScene.
    /// Surcharger dans les classes enfants si besoin (ex: désactiver le XR Rig local).
    /// </summary>
    protected virtual void OnSharedSceneLoaded() { }

    // ─── Déconnexion ──────────────────────────────────────────────────────────

    protected async void OnApplicationQuit()
    {
        if (_session == null) return;

        var sessionToLeave = _session;
        _session = null;
        _state   = ConnectionState.Disconnected;

        try
        {
            await sessionToLeave.LeaveAsync();
        }
        catch (ObjectDisposedException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[{GetType().Name}] LeaveAsync exception: {e.Message}");
        }
    }

    protected virtual void OnDestroy()
    {
        if (m_NetworkManager != null && m_NetworkManager.SceneManager != null)
            m_NetworkManager.SceneManager.OnLoadComplete -= OnNetworkSceneLoadComplete;

        _session = null;
    }
}