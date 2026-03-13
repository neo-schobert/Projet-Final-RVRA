using UnityEngine;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;

/// <summary>
/// Cas 2 — scène partagée unique pour AR et VR.
///
/// Ce script gère :
///   1. La connexion à la session Unity (CreateOrJoin → premier arrivé crée, les autres rejoignent)
///   2. Le spawn du bon prefab joueur selon la plateforme (VR ou AR)
///
/// À placer sur le NetworkManager dans Scene.unity.
/// </summary>
public class SharedSceneManager : MonoBehaviour
{
    [Header("Session")]
    [Tooltip("Nom de la session partagée. Doit être identique sur les deux devices.")]
    [SerializeField] private string sessionName = "Session_RVRA";
    [SerializeField] private int maxPlayers = 2;

    [Header("Prefabs joueurs")]
    [Tooltip("Prefab spawné quand le joueur local est en VR (casque)")]
    [SerializeField] private GameObject vrPlayerPrefab;
    [Tooltip("Prefab spawné quand le joueur local est en AR (téléphone/tablette)")]
    [SerializeField] private GameObject arPlayerPrefab;

    private async void Start()
    {
        await ConnecterEtSpawner();
    }

    private async Task ConnecterEtSpawner()
    {
        try
        {
            // 1. Initialiser les services Unity Cloud
            await UnityServices.InitializeAsync();

            // 2. Connexion anonyme
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            Debug.Log($"[SharedSceneManager] Connecté en tant que : {AuthenticationService.Instance.PlayerId}");

            // 3. CreateOrJoin : le premier crée, le second rejoint automatiquement
            var options = new SessionOptions()
            {
                Name = sessionName,
                MaxPlayers = maxPlayers
            }.WithDistributedAuthorityNetwork();

            var session = await MultiplayerService.Instance.CreateOrJoinSessionAsync(sessionName, options);
            Debug.Log($"[SharedSceneManager] Session '{sessionName}' rejointe/créée !");

            // 4. Spawner le bon prefab selon la plateforme locale
            SpawnerJoueurLocal();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SharedSceneManager] Erreur de connexion : {e.Message}");
        }
    }

    private void SpawnerJoueurLocal()
    {
        // XRRigSwitcher.IsVRMode est défini dans Awake() avant que Start() soit appelé
        bool estVR = XRRigSwitcher.IsVRMode;
        GameObject prefab = estVR ? vrPlayerPrefab : arPlayerPrefab;

        if (prefab == null)
        {
            Debug.LogError($"[SharedSceneManager] Le prefab {(estVR ? "VR" : "AR")} n'est pas assigné dans l'Inspector !");
            return;
        }

        // Instancie et spawn sur le réseau (en mode Distributed Authority, c'est le client qui spawne lui-même)
        GameObject joueur = Instantiate(prefab);
        joueur.GetComponent<NetworkObject>().Spawn();

        Debug.Log($"[SharedSceneManager] Joueur {(estVR ? "VR" : "AR")} spawné !");
    }
}
