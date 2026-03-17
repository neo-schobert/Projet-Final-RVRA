using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// À placer sur le GameObject "TempleSceneManager" dans TempleScene.
///
/// Responsabilités (exécutées dans Start, après que XRRigSwitcher ait activé le bon rig) :
///   1. Positionne les joueurs dans TempleScene selon la calibration sauvegardée.
///        • AR : XR Origin positionné pour que Camera.main AR soit à (0, 0, 0).
///              Formule : XROrigin_AR.position = Vector3.zero - arCamera.localPosition
///        • VR : XR Origin positionné pour que Camera.main VR soit à (SpawnDistance, 0, 0),
///              regardant vers (0, 0, 0).
///              Formule : XROrigin_VR.position = (SpawnDistance, 0, 0) - vrCamera.localPosition
///              SpawnDistance = magnitude XZ de l'offset physique VR−AR (CalibrationPersistence).
///   2. Si VR : désactive tous les LocomotionProviders (Move + Teleport + Turn)
///      pour empêcher le joueur VR de se déplacer — le tracking physique reste actif.
///
/// SETUP dans TempleScene :
///   A. GameObject "TempleSceneManager" avec ce script attaché.
///   B. Hiérarchie XR identique à Scene.unity :
///        - XRRigSwitcher (rigs désactivés par défaut) → active le bon rig dans Awake().
///        - XR Origin (VR) désactivé par défaut.
///        - AR Session + XR Origin (Mobile AR) désactivés par défaut.
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

        PositionnerJoueur();

        if (XRRigSwitcher.IsVRMode)
            DesactiverLocomotionVR();
    }

    // ── Positionnement des joueurs ─────────────────────────────────────────────

    private void PositionnerJoueur()
    {
        if (XRRigSwitcher.IsVRMode)
            PositionnerVR();
        else
            PositionnerAR();
    }

    /// <summary>
    /// Positionne le XR Origin VR pour que Camera.main VR soit à (SpawnDistance, ?, 0)
    /// en XZ, puis oriente le XR Origin VR vers (0, 0, 0).
    /// Formule : XROrigin_VR.position = (SpawnDistance, 0, 0) - vrCamera.localPosition
    /// Si aucune donnée de calibration n'est disponible, le XR Origin VR n'est pas modifié.
    /// </summary>
    private void PositionnerVR()
    {
        Transform vrOrigin = ObtenirVROrigin();
        if (vrOrigin == null)
        {
            Debug.LogWarning("[TempleSceneManager] XR Origin VR introuvable — spawn VR ignoré.");
            return;
        }

        if (!CalibrationPersistence.HasData)
        {
            Debug.LogWarning("[TempleSceneManager] Aucune donnée de calibration disponible. " +
                             "Le joueur VR sera positionné sans calibration.");
            return;
        }

        Camera vrCam = Camera.main;
        Vector3 camLocalPos = vrCam != null ? vrCam.transform.localPosition : Vector3.zero;

        // Positionne l'origine pour que la caméra VR soit à (SpawnDistance, ?, 0) en XZ
        vrOrigin.position = CalibrationPersistence.VRSpawnPosition - camLocalPos;
        // Oriente le XR Origin VR vers l'ancre AR à (0, 0, 0)
        vrOrigin.rotation = CalibrationPersistence.VRSpawnRotation;

        Debug.Log($"[TempleSceneManager] ✓ Joueur VR positionné — " +
                  $"XROrigin={vrOrigin.position:F3} " +
                  $"(spawnDist={CalibrationPersistence.SpawnDistance:F3}m) " +
                  $"rot={vrOrigin.rotation.eulerAngles:F1}");
    }

    /// <summary>
    /// Positionne le XR Origin AR pour que Camera.main AR soit à (0, 0, 0) en XZ.
    /// Formule : XROrigin_AR.position = Vector3.zero - arCamera.localPosition
    /// </summary>
    private void PositionnerAR()
    {
        Transform arOrigin = ObtenirAROrigin();
        if (arOrigin == null)
        {
            Debug.LogWarning("[TempleSceneManager] XR Origin AR introuvable — spawn AR ignoré.");
            return;
        }

        Camera arCam = Camera.main;
        Vector3 camLocalPos = arCam != null ? arCam.transform.localPosition : Vector3.zero;

        // Positionne l'origine pour que la caméra AR soit à (0, ?, 0) en XZ
        arOrigin.position = Vector3.zero - camLocalPos;
        arOrigin.rotation = Quaternion.identity;

        Debug.Log($"[TempleSceneManager] ✓ Joueur AR positionné à l'ancre (0, 0, 0) — " +
                  $"XROrigin={arOrigin.position:F3}");
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
        var providers = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        if (providers.Length == 0)
        {
            Debug.LogWarning("[TempleSceneManager] Aucun LocomotionProvider trouvé dans TempleScene. " +
                             "Vérifie que le XR Origin VR est bien configuré avec ses providers.");
            return;
        }

        foreach (var p in providers)
        {
            p.enabled = false;
            Debug.Log($"[TempleSceneManager] Locomotion désactivée : {p.GetType().Name} " +
                      $"sur '{p.gameObject.name}'");
        }

        Debug.Log($"[TempleSceneManager] ✓ {providers.Length} LocomotionProvider(s) désactivé(s). " +
                  "Le joueur VR ne peut plus se déplacer — tracking physique conservé.");
    }
}
