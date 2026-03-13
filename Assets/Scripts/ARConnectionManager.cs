using UnityEngine;
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
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            var options = new SessionOptions()
            {
                MaxPlayers = 4,
                Name = "Session_RVRA"
            };
            options.WithDistributedAuthorityNetwork();

            await MultiplayerService.Instance.CreateSessionAsync(options);
            Debug.Log("[ARConnectionManager] Session AR créée. En attente du joueur VR...");

            // Cas 2 : LSJ est déjà la scène active (AR démarre directement dedans).
            // XRPlatformSetup dans LSJ a déjà activé le rig AR au démarrage.
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ARConnectionManager] Erreur : {e.Message}");
        }
    }
}
