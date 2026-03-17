using Unity.XR.CoreUtils;
using UnityEngine;


/// <summary>
/// À placer sur un GameObject vide "TempleSceneManager" dans TempleScene.
///
/// Responsabilités (exécutées dans Start, après que XRRigSwitcher ait activé le bon rig) :
///   1. Applique la calibration sauvegardée (CalibrationPersistence) au XR Origin local.
///   2. Si VR : désactive tous les LocomotionProviders (Move + Teleport + Turn) pour
///      empêcher le joueur VR de se déplacer — le tracking physique de la tête reste actif.
///
/// SETUP dans TempleScene :
///   A. Créer un GameObject "TempleSceneManager" et y attacher ce script.
///   B. Copier / recréer la hiérarchie XR de Scene.unity :
///        - XRRigSwitcher (ou XRPlatformSetup) → même config, rigs désactivés par défaut.
///        - XR Origin (VR) désactivé par défaut.
///        - AR Session + XR Origin (Mobile AR) désactivés par défaut.
///        XRRigSwitcher.Awake() activera le bon rig AVANT Start() de ce script.
///   C. Assigner _vrXROrigin et _arXROrigin dans l'Inspector
///      (laissez vide pour auto-détection via FindAnyObjectByType<XROrigin>()).
/// </summary>
public class TempleSceneManager : MonoBehaviour
{
    [Header("XR Origins de TempleScene (optionnel — auto-détectés si non assignés)")]
    [Tooltip("XR Origin VR de TempleScene. Laissez vide pour auto-détection.")]
    [SerializeField] private Transform _vrXROrigin;

    [Tooltip("XR Origin AR (Mobile AR) de TempleScene. Laissez vide pour auto-détection.")]
    [SerializeField] private Transform _arXROrigin;

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // XRRigSwitcher.Awake() s'est déjà exécuté → IsVRMode est défini
        // et le bon rig est actif. FindAnyObjectByType<XROrigin>() trouve donc le bon.

        Debug.Log($"[TempleSceneManager] Démarrage — mode : {(XRRigSwitcher.IsVRMode ? "VR" : "AR")}");

        AppliquerCalibration();

        if (XRRigSwitcher.IsVRMode)
            DesactiverLocomotionVR();
    }

    // ── Calibration ───────────────────────────────────────────────────────────

    private void AppliquerCalibration()
    {
        if (XRRigSwitcher.IsVRMode)
        {
            // Côté VR : applique la position/rotation sauvegardée du XR Origin VR
            Transform vrOrigin = ObtenirVROrigin();
            if (vrOrigin != null)
                CalibrationPersistence.ApplyVR(vrOrigin);
            else
                Debug.LogWarning("[TempleSceneManager] XR Origin VR introuvable — calibration VR non appliquée.");
        }
        else
        {
            // Côté AR : applique la position/rotation sauvegardée du XR Origin AR
            Transform arOrigin = ObtenirAROrigin();
            if (arOrigin != null)
                CalibrationPersistence.ApplyAR(arOrigin);
            else
                Debug.LogWarning("[TempleSceneManager] XR Origin AR introuvable — calibration AR non appliquée.");
        }
    }

    // ── Helpers XR Origin ─────────────────────────────────────────────────────

    /// <summary>
    /// Renvoie le Transform du XR Origin VR (Inspector → auto-détection).
    /// En mode VR seul le VR XROrigin est actif → FindAnyObjectByType le trouve sans ambiguïté.
    /// </summary>
    private Transform ObtenirVROrigin()
    {
        if (_vrXROrigin != null)
            return _vrXROrigin;

        XROrigin found = FindAnyObjectByType<XROrigin>();
        if (found != null)
        {
            Debug.Log($"[TempleSceneManager] VR XR Origin auto-détecté : '{found.name}'");
            return found.transform;
        }
        return null;
    }

    /// <summary>
    /// Renvoie le Transform du XR Origin AR (Inspector → auto-détection).
    /// En mode AR seul l'AR XROrigin est actif → FindAnyObjectByType le trouve sans ambiguïté.
    /// </summary>
    private Transform ObtenirAROrigin()
    {
        if (_arXROrigin != null)
            return _arXROrigin;

        XROrigin found = FindAnyObjectByType<XROrigin>();
        if (found != null)
        {
            Debug.Log($"[TempleSceneManager] AR XR Origin auto-détecté : '{found.name}'");
            return found.transform;
        }
        return null;
    }

    // ── Locomotion VR ─────────────────────────────────────────────────────────

    /// <summary>
    /// Désactive tous les LocomotionProvider de la scène :
    ///   ContinuousMoveProvider, TeleportationProvider, SnapTurnProvider, etc.
    ///
    /// Le tracking physique de la tête ET des mains reste actif : il est géré
    /// par XR Origin / Input System, indépendamment des LocomotionProviders.
    ///
    /// FindObjectsByType avec FindObjectsInactive.Include cherche aussi les composants
    /// sur des GameObjects inactifs (utile si le rig est partiellement désactivé).
    /// </summary>
    private void DesactiverLocomotionVR()
    {
        UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider[] providers = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        if (providers.Length == 0)
        {
            Debug.LogWarning("[TempleSceneManager] Aucun LocomotionProvider trouvé dans TempleScene. " +
                             "Vérifie que le XR Origin VR est bien configuré avec ses providers.");
            return;
        }

        foreach (UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider p in providers)
        {
            p.enabled = false;
            Debug.Log($"[TempleSceneManager] Locomotion désactivée : {p.GetType().Name} " +
                      $"sur '{p.gameObject.name}'");
        }

        Debug.Log($"[TempleSceneManager] ✓ {providers.Length} LocomotionProvider(s) désactivé(s). " +
                  "Le joueur VR ne peut plus se déplacer — tracking physique conservé.");
    }
}
