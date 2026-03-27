using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gestionnaire de fin de partie dans TempleScene.
/// Place ce script sur un GameObject NetworkObject dans la scène.
///
/// Deux cas de fin :
///   • Balle VR touche le joueur AR  → VR gagne, AR perd   → DeclareVRWins()
///   • Munitions à 0                 → AR gagne, VR perd   → DeclareARWins()
///
/// SETUP Inspector :
///   vrVictoryMessage  → "EndMessage" victoire  sur la caméra VR (WorldSpace/Overlay)
///   vrDefeatMessage   → "EndMessage" défaite   sur la caméra VR
///   arVictoryCanvas   → Canvas victoire  pour le joueur AR
///   arDefeatCanvas    → Canvas défaite   pour le joueur AR
/// </summary>
public class TempleEndGame : NetworkBehaviour
{
    public static TempleEndGame Instance { get; private set; }

    [Header("VR — messages affichés sur la caméra VR")]
    [Tooltip("GameObject du message de victoire VR (ex : EndMessage 'Vous avez gagné')")]
    public GameObject vrVictoryMessage;

    [Tooltip("GameObject du message de défaite VR")]
    public GameObject vrDefeatMessage;

    [Header("AR — canvas affiché sur le téléphone AR")]
    [Tooltip("Canvas (ou panel) de victoire AR")]
    public GameObject arVictoryCanvas;

    [Tooltip("Canvas (ou panel) de défaite AR")]
    public GameObject arDefeatCanvas;

    // Empêche d'afficher plusieurs fois le message si plusieurs triggers arrivent
    private bool _gameEnded = false;

    private void Awake()
    {
        Instance = this;

        // Tout cacher au départ
        vrVictoryMessage?.SetActive(false);
        vrDefeatMessage?.SetActive(false);
        arVictoryCanvas?.SetActive(false);
        arDefeatCanvas?.SetActive(false);
    }

    // ── Déclencheurs publics ──────────────────────────────────────────────────

    /// <summary>
    /// Appelé par Bullet.cs quand la balle touche le joueur AR.
    /// N'appeler que depuis le client VR (IsVRMode == true).
    /// </summary>
    public void DeclareVRWins()
    {
        if (_gameEnded) return;
        _gameEnded = true;
        ShowEndGameRpc(true);
    }

    /// <summary>
    /// Appelé par Shoot.cs quand les munitions atteignent 0.
    /// N'appeler que depuis l'owner du gun (IsOwner == true).
    /// </summary>
    public void DeclareARWins()
    {
        if (_gameEnded) return;
        _gameEnded = true;
        ShowEndGameRpc(false);
    }

    // ── RPC broadcast ─────────────────────────────────────────────────────────

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    private void ShowEndGameRpc(bool vrWins)
    {
        _gameEnded = true; // bloque un éventuel double-trigger local

        if (XRRigSwitcher.IsVRMode)
        {
            // Joueur VR — messages en World Space sur la caméra
            vrVictoryMessage?.SetActive(vrWins);
            vrDefeatMessage?.SetActive(!vrWins);
        }
        else
        {
            // Joueur AR — canvas plein écran
            arVictoryCanvas?.SetActive(!vrWins);  // AR gagne quand VR perd
            arDefeatCanvas?.SetActive(vrWins);    // AR perd  quand VR gagne
        }

        Debug.Log($"[TempleEndGame] Fin de partie — VR {(vrWins ? "GAGNE" : "PERD")}");
    }
}
