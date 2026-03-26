using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Corrige le crash NullReferenceException dans NetworkSceneManager.Dispose()
/// qui apparaît à l'arrêt de l'application (ou stop du mode Play en éditeur).
///
/// CAUSE :
///   NetworkManager.OnApplicationQuit() → ShutdownInternal() → NetworkSceneManager.Dispose()
///   appelle Dispose() sur des objets déjà null si la scène est en cours de démontage.
///   C'est un bug connu dans certaines versions de NGO + Distributed Authority.
///
/// FIX :
///   On appelle Shutdown() manuellement AVANT qu'Unity détruise les GameObjects.
///   Ça donne à NGO le temps de nettoyer ses références internes proprement.
///
/// SETUP :
///   Mettre ce script sur un GameObject persistant (ex: NetworkManager lui-même,
///   ou un GameObject dédié dans la scène de démarrage).
/// </summary>
public class NetworkManagerCleanup : MonoBehaviour
{
    private bool _alreadyShutdown = false;

    private void OnApplicationQuit()
    {
        ShutdownNow();
    }

    // Couvre aussi le cas "Stop" en mode Play dans l'éditeur
    private void OnDestroy()
    {
        ShutdownNow();
    }

    private void ShutdownNow()
    {
        if (_alreadyShutdown) return;
        _alreadyShutdown = true;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.Log("[NetworkManagerCleanup] Shutdown propre avant destruction.");
            NetworkManager.Singleton.Shutdown();
        }
    }
}
