using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;

public class VRConnectionManager : MonoBehaviour
{
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

        Debug.Log("Recherche de la session AR en cours...");

        try
        {
            // Cherche les sessions actives créées par le téléphone
            var queryOptions = new QuerySessionsOptions();
            var resultats = await MultiplayerService.Instance.QuerySessionsAsync(queryOptions);

            // CORRECTION ICI : On utilise resultats.Sessions.Count et resultats.Sessions[0]
            if (resultats.Sessions.Count > 0)
            {
                string sessionId = resultats.Sessions[0].Id;
                Debug.Log($"Session trouvée ! Connexion en cours vers {sessionId}...");
                await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId);
            }
            else
            {
                Debug.LogError("Aucune session AR trouvée. Vérifie que l'AR est bien lancée et connectée à internet !");
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