using UnityEngine;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;

public class ARConnectionManager : MonoBehaviour
{
    async void Start()
    {
        await LancerSessionDistribuee();
    }

    private async Task LancerSessionDistribuee()
    {
        try
        {
            // 1. Initialiser les services Cloud d'Unity
            await UnityServices.InitializeAsync();

            // 2. Se connecter de manière anonyme
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            // 3. Créer la session pour l'Autorité Distribuée
            var options = new SessionOptions()
            {
                MaxPlayers = 4,
                Name = "Session_RVRA" // Un nom unique pour la retrouver
            };

            await MultiplayerService.Instance.CreateSessionAsync(options);
            Debug.Log("Session AR (Autorité Distribuée) créée avec succès sur les serveurs d'Unity !");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Erreur lors de la création de la session AR : {e.Message}");
        }
    }
}