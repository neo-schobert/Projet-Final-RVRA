using UnityEngine;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;
using System;
using UnityEngine.SceneManagement;

/// <summary>
/// Scène partagée AR + VR — topologie Distributed Authority (DA).
///
/// En DA : il n'y a pas de "host" dédié. Chaque client a autorité sur ses propres objets.
///   → VR et AR appellent tous les deux CreateOrJoinSessionAsync avec le même sessionName
///   → Le premier créé la session DA, le second la rejoint
///   → Chaque client spawne son propre joueur dans OnClientConnected (clientId == LocalClientId)
///
/// IMPORTANT (à faire dans Unity Editor) :
///   1. NetworkManager Inspector → Player Prefab = AUCUN (None)
///   2. SharedCube NetworkObject Inspector → Ownership Status = Distributable
///   3. Unity Dashboard → Multiplayer → Distributed Authority → ACTIVÉ
///
/// À placer sur le NetworkManager dans Scene.unity.
/// </summary>
public class SharedSceneManager : MonoBehaviour
{
    [Header("Session")]
    [Tooltip("Nom de la session partagée. Identique sur les deux devices.")]
    [SerializeField] private string sessionName = "Session_RVRA_DA3";
    [SerializeField] private int maxPlayers = 2;

    [Tooltip("Nombre max de tentatives")]
    [SerializeField] private int maxRetries = 8;

    [Tooltip("Délai entre tentatives (s)")]
    [SerializeField] private float retryDelay = 3f;

    [Tooltip("Délai initial pour l'AR — laisse au VR le temps de créer la session DA (s)")]
    [SerializeField] private float arInitialWaitDelay = 15f;

    [Header("Prefabs joueurs")]
    [Tooltip("Prefab du joueur VR")]
    [SerializeField] private GameObject vrPlayerPrefab;
    [Tooltip("Prefab du joueur AR")]
    [SerializeField] private GameObject arPlayerPrefab;

    private ISession _session;

    // FIX: Nom de la scène de destination — utilisé pour détecter l'arrivée dans TempleScene.
    [Header("Transition de scène")]
    [Tooltip("Nom exact de la scène de destination (doit correspondre à Build Settings).")]
    [SerializeField] private string templeSceneName = "TempleScene";

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        else
            Debug.LogError("[SharedSceneManager] NetworkManager introuvable dans la scène !");

        // FIX: Abonnement à sceneLoaded pour re-spawner le joueur après transition de scène.
        // Raison : en mode DA, SceneManager.LoadScene() bypass la migration NGO des NetworkObjects
        // → VRPlayer (et éventuellement PlayerAR) sont détruits au changement de scène.
        // SharedSceneManager survit (sur NetworkManager DontDestroyOnLoad) et re-spawne si besoin.
        SceneManager.sceneLoaded += OnSceneLoaded;

        _ = ConnecterAsync();
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        // FIX: Désabonnement propre.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_session != null)
            _ = _session.LeaveAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FIX: RE-SPAWN après transition de scène
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appelé après chaque chargement de scène (Awake des objets de scène déjà exécutés).
    /// Si on arrive dans TempleScene et que le joueur local n'a plus de NetworkObject,
    /// on le re-spawne immédiatement.
    ///
    /// Guard : si le joueur a migré via SceneMigrationSynchronization (cas idéal),
    /// PlayerObject != null → on ne re-spawne pas → pas de doublon.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // On n'agit que sur la scène de destination
        if (scene.name != templeSceneName) return;

        // Session et connexion nécessaires
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            Debug.LogWarning("[SharedSceneManager] sceneLoaded dans TempleScene mais pas connecté — spawn ignoré.");
            return;
        }

        // FIX: Chercher si un NetworkObject joueur local existe déjà dans la scène
        // (migration réussie via SceneMigrationSynchronization) OU dans DontDestroyOnLoad.
        // On utilise IsOwner + IsPlayerObject plutôt que LocalClient.PlayerObject
        // car en DA l'assignation PlayerObject peut être asynchrone.
        bool joueurExiste = false;
        foreach (var netObj in FindObjectsByType<NetworkObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (netObj.IsOwner && netObj.IsPlayerObject)
            {
                joueurExiste = true;
                Debug.Log($"[SharedSceneManager] Joueur local déjà présent dans TempleScene : '{netObj.name}' " +
                          "(migration réussie — pas de re-spawn).");
                break;
            }
        }

        if (!joueurExiste)
        {
            Debug.Log("[SharedSceneManager] Joueur local introuvable dans TempleScene → re-spawn.");
            SpawnLocalPlayer();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SPAWN — chaque client spawne son propre joueur (DA : pas de host dédié)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// En DA, ce callback fire sur tous les clients connectés quand un nouveau client arrive.
    /// On n'agit QUE pour notre propre connexion (clientId == LocalClientId).
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        // On ne spawne que pour soi-même
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        SpawnLocalPlayer();
    }

    private void SpawnLocalPlayer()
    {
        bool isVR   = XRRigSwitcher.IsVRMode;
        GameObject prefab = isVR ? vrPlayerPrefab : arPlayerPrefab;

        if (prefab == null)
        {
            Debug.LogError($"[SharedSceneManager] Prefab {(isVR ? "VR" : "AR")} non assigné dans l'Inspector !");
            return;
        }

        // En DA, le client qui appelle Spawn() obtient l'autorité sur l'objet spawné
        GameObject joueur = Instantiate(prefab);
        joueur.GetComponent<NetworkObject>().SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId);

        Debug.Log($"[SharedSceneManager] ✓ Joueur {(isVR ? "VR" : "AR")} spawné localement (DA).");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONNEXION
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ConnecterAsync()
    {
        // 1. Initialiser Unity Services
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            Debug.Log($"[SharedSceneManager] Authentifié : {AuthenticationService.Instance.PlayerId}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SharedSceneManager] Erreur init services : {e.Message}");
            return;
        }

        // 2. Options Distributed Authority
        //    PRÉREQUIS : Unity Dashboard → Multiplayer → Distributed Authority → Activé
        var options = new SessionOptions()
        {
            MaxPlayers = maxPlayers
        }.WithDistributedAuthorityNetwork();

        // 3. L'AR attend que le VR ait créé la session DA en premier
        if (!XRRigSwitcher.IsVRMode)
        {
            Debug.Log($"[SharedSceneManager] Device AR → attente {arInitialWaitDelay}s " +
                      "(VR doit créer la session en premier)...");
            await Task.Delay(TimeSpan.FromSeconds(arInitialWaitDelay));
        }

        // 4. Boucle de connexion
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Debug.Log($"[SharedSceneManager] Tentative {attempt}/{maxRetries} → '{sessionName}'...");
                _session = await MultiplayerService.Instance.CreateOrJoinSessionAsync(sessionName, options);
                Debug.Log("[SharedSceneManager] ✓ Session DA rejointe/créée !");
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SharedSceneManager] Tentative {attempt} échouée : {e.Message} ({e.GetType().Name})");

                if (attempt >= maxRetries)
                {
                    Debug.LogError("[SharedSceneManager] Échec connexion après toutes les tentatives.\n" +
                                   "VÉRIFIER : 1) Unity Dashboard → Distributed Authority ACTIVÉ  " +
                                   "2) Même sessionName sur les deux devices  " +
                                   "3) Connexion Internet active.");
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(retryDelay));
            }
        }
    }
}
