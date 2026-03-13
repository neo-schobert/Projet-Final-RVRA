using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;

public class VRConnectionManager : MonoBehaviour
{
    // C'est ici que tu définis le nom exact que le casque doit chercher
    public string nomDeLaSessionAR = "Session_RVRA";

    private bool isConnecting = false;

    async void Start()
    {
        // On prépare les services dès l'ouverture de la scène VR
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    public async void JoinSharedWorld()
    {
        // Sécurité pour éviter de lancer la connexion plusieurs fois
        if (isConnecting) return;
        isConnecting = true;

        Debug.Log($"Recherche de la session spécifique nommée : {nomDeLaSessionAR}...");

        try
        {
            // On récupère toutes les sessions actives
            var queryOptions = new QuerySessionsOptions();
            var resultats = await MultiplayerService.Instance.QuerySessionsAsync(queryOptions);

            string sessionIdTrouve = "";

            // NOUVEAU : On filtre la liste pour trouver UNIQUEMENT la bonne session
            foreach (var session in resultats.Sessions)
            {
                if (session.Name == nomDeLaSessionAR)
                {
                    sessionIdTrouve = session.Id;
                    break; // On a trouvé la bonne, on arrête de chercher !
                }
            }

            // Si on a bien trouvé l'ID de notre session "Session_RVRA"
            if (!string.IsNullOrEmpty(sessionIdTrouve))
            {
                Debug.Log($"Session '{nomDeLaSessionAR}' trouvée ! Connexion en cours...");
                await MultiplayerService.Instance.JoinSessionByIdAsync(sessionIdTrouve);
                Debug.Log("🚀 SUCCÈS : Le casque VR est entré dans la partie réseau !");
            }
            else
            {
                Debug.LogError($"Aucune session nommée '{nomDeLaSessionAR}' n'a été trouvée. L'AR est-elle bien lancée ?");
                isConnecting = false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Erreur de connexion VR : {e.Message}");
            isConnecting = false;
        }
    }
}
