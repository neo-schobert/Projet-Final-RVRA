using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// À placer sur le GameObject "TempleSceneManager" dans TempleScene.
///
/// Responsabilités (exécutées dans Start, après que XRRigSwitcher ait activé le bon rig) :
///   1. Positionne les joueurs dans TempleScene selon la calibration sauvegardée.
///        • VR : XROrigin.position = (VrPosition.x, 0, VrPosition.z)
///               XROrigin.rotation = Euler(0, VrPhysicalYaw, 0)
///        • AR : XROrigin.position = (ArPhysicalPosition.x, 0, ArPhysicalPosition.z)
///               XROrigin.rotation = Euler(0, ArYaw, 0)
///        Pas de conversion d'espace — les positions physiques sont utilisées directement.
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

    [SerializeField] private Transform _gameboard;

    [Tooltip("Décalage angulaire Y supplémentaire après l'alignement (degrés).\n" +
             "Utilise 0  si l'axe de jeu du gameboard est son +X local.\n" +
             "Utilise 90 si l'axe de jeu est son +Z local (forward Unity par défaut).")]
    [SerializeField] private float _gameboardRotationOffset = 0f;

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // XRRigSwitcher.Awake() s'est déjà exécuté → IsVRMode est défini,
        // le bon rig est actif sur ce device.
        Debug.Log($"[TempleSceneManager] Démarrage — mode : {(XRRigSwitcher.IsVRMode ? "VR" : "AR")}");

        // ── 1. Positionner les DEUX XR Origins ───────────────────────────────
        PositionnerVR();
        PositionnerAR();

        // ── 2. Désactiver locomotion (VR : tracking physique uniquement) ─────
        if (XRRigSwitcher.IsVRMode)
            DesactiverLocomotionVR();

        // ── 3. Orienter le gameboard selon la direction VR→AR ────────────────
        AlignGameboard();

        // Note : les players NetworkObjects sont positionnés automatiquement par
        // PlayerSetup.Update() qui suit Camera.main chaque frame.
        // Il suffit que les XR Origins soient bien placés (fait ci-dessus).
    }

    /// <summary>
    /// Aligne l'axe +X du gameboard sur la direction VR→AR dans l'espace TempleScene.
    ///
    /// CONVERSION OBLIGATOIRE physique → virtuel :
    ///   SpawnARPosition est en espace physique monde (Scene.unity).
    ///   Dans TempleScene, VR XR Origin a la rotation VRSpawnRotationDirect = Euler(0,vrYaw,0).
    ///   Cette rotation mappe l'espace physique → espace virtuel TempleScene.
    ///   Tout vecteur physique d doit être converti : d_virtuel = VRSpawnRotationDirect * d.
    ///   Sans cette conversion, l'erreur angulaire = vrYaw (ex : 36° → gameboard à 144° au lieu de 180°).
    ///
    /// Calcul direct de l'angle :
    ///   angle = SignedAngle(Vector3.right, toAR_virtuel, Vector3.up)
    ///   gameboard.rotation = Euler(0, angle + offset, 0)
    ///   → +X du gameboard pointe exactement vers AR dans l'espace virtuel.
    /// </summary>
    private void AlignGameboard()
    {
        if (_gameboard == null)
        {
            Debug.LogWarning("[TempleSceneManager] _gameboard non assigné dans l'Inspector — " +
                             "gameboard non orienté.");
            return;
        }

        if (!CalibrationPersistence.HasData)
        {
            Debug.LogWarning("[TempleSceneManager] HasData=false — gameboard non orienté.\n" +
                             "Cause probable : la calibration n'a pas été synchronisée vers ce device.\n" +
                             "Vérifie que PlayerSetup.SyncCalibrationRpc est bien envoyé depuis AR.");
            return;
        }

        // Direction VR→AR en XZ (positions directement stockées par la calibration)
        Vector3 vrPos = CalibrationPersistence.VrPosition;
        Vector3 arPos = CalibrationPersistence.ArPhysicalPosition;
        Vector3 toAR  = new Vector3(arPos.x - vrPos.x, 0f, arPos.z - vrPos.z);

        if (toAR.sqrMagnitude < 0.001f)
        {
            Debug.LogWarning($"[TempleSceneManager] toAR ≈ 0 — VR et AR semblent au même endroit. " +
                             $"vrPos={vrPos:F3}  arPos={arPos:F3}");
            return;
        }

        float angle = Vector3.SignedAngle(Vector3.right, toAR.normalized, Vector3.up);
        _gameboard.rotation = Quaternion.Euler(0f, angle + _gameboardRotationOffset, 0f);

        Debug.Log($"[TempleSceneManager] ✓ Gameboard aligné — " +
                  $"vrPos={vrPos:F3}  arPos={arPos:F3}  toAR={toAR:F2}  " +
                  $"angle={angle:F1}°  rot={_gameboard.rotation.eulerAngles:F1}°");
    }

    // ── Positionnement des joueurs ─────────────────────────────────────────────

    // PositionnerJoueur() supprimé : Start() appelle directement PositionnerVR()
    // et PositionnerAR() sur chaque device afin que les deux XR Origins soient
    // positionnés (même l'inactif) — nécessaire pour AlignGameboard().

    /// <summary>
    /// Positionne le XR Origin VR pour que Camera.main VR soit à (0, 0, 0) — ancre de référence.
    /// Oriente le XR Origin VR vers (SpawnDistance, 0, 0) (vers le joueur AR).
    /// Formule : XROrigin_VR.position = (0, 0, 0) - vrCamera.localPosition
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

        vrOrigin.position = new Vector3(0f, 0f, 0f);
        vrOrigin.rotation = Quaternion.Euler(0f, CalibrationPersistence.VrPhysicalYaw, 0f);
    }

    /// <summary>
    /// Positionne le XR Origin AR dans TempleScene :
    ///   • Remet la scale à (1,1,1)  — annule la vue aérienne (scale 0.01) du mini-monde.
    ///   • Place la caméra AR à (ARSpawnPosition) = (SpawnDistance, 0, 0), regardant vers (0,0,0).
    ///   • Oriente le XR Origin AR selon ARSpawnRotation (vers le joueur VR à l'origine).
    ///
    /// Formule : XROrigin_AR.position = ARSpawnPosition - arCamera.localPosition
    /// </summary>
    private void PositionnerAR()
    {
        Transform arOrigin = ObtenirAROrigin();
        if (arOrigin == null)
        {
            Debug.LogWarning("[TempleSceneManager] XR Origin AR introuvable — spawn AR ignoré.");
            return;
        }

        // ── 1. Annuler la scale de vue aérienne ───────────────────────────────
        // En vue aérienne, ARProximityCalibration applique localScale = 0.01.
        // Le XR Origin de TempleScene est neuf (nouvelle scène) mais on reset
        // explicitement pour garantir une vitesse de déplacement normale (1:1).
        arOrigin.localScale = Vector3.one;

        // ── 2. Positionner selon la calibration ──────────────────────────────
        if (!CalibrationPersistence.HasData)
        {
            Debug.LogWarning("[TempleSceneManager] HasData=false — AR non positionné.");
            return;
        }

        Vector3 arPos = CalibrationPersistence.ArPhysicalPosition;
        Vector3 vrPos = CalibrationPersistence.VrPosition;
        Vector3 buggedPositionOffset = new Vector3(-0.325006f, 0f, 8.078408f);
        float buggedYawOffset = 328f;

        Vector3    targetPos = new Vector3(arPos.x - vrPos.x + buggedPositionOffset.x, 0f, arPos.z - vrPos.z + buggedPositionOffset.z);
        Quaternion targetRot = Quaternion.Euler(0f, CalibrationPersistence.ArYaw + buggedYawOffset, 0f);

        arOrigin.rotation = targetRot;

        // Sur le device AR : la caméra ARCore a un localPosition non-nul
        // (offset physique depuis le démarrage de la session ARCore).
        // Si on place le XR Origin à targetPos, la caméra se retrouve à
        // targetPos + rot * cam.localPosition → position décalée.
        // Fix : on compense l'offset XZ pour que la CAMÉRA atterrisse à targetPos.
        // Sur le device VR : pas de caméra AR → on place le XR Origin à targetPos
        // (utile uniquement pour AlignGameboard, l'AR XR Origin est inactif côté VR).
        if (!XRRigSwitcher.IsVRMode)
        {
            Camera arCam = Camera.main;
            if (arCam != null)
            {
                Vector3 camLocalFlat   = new Vector3(arCam.transform.localPosition.x, 0f,
                                                     arCam.transform.localPosition.z);
                Vector3 camWorldOffset = targetRot * camLocalFlat;
                arOrigin.position = new Vector3(targetPos.x - camWorldOffset.x, 0f,
                                                targetPos.z - camWorldOffset.z);
                Debug.Log($"[TempleSceneManager] AR: XROrigin compensé — " +
                          $"camLocalXZ={camLocalFlat:F3}  offset={camWorldOffset:F3}  " +
                          $"XROrigin={arOrigin.position:F3}");
            }
            else
            {
                arOrigin.position = targetPos;
                Debug.LogWarning("[TempleSceneManager] Camera.main introuvable sur AR — " +
                                 "XROrigin placé sans compensation localPosition.");
            }
        }
        else
        {
            arOrigin.position = targetPos;
        }

        Debug.Log($"[TempleSceneManager] ✓ XR Origin AR positionné — " +
                  $"targetPos={targetPos:F3}  arYaw={CalibrationPersistence.ArYaw:F1}°");
    }

    // ── Helpers XR Origin ─────────────────────────────────────────────────────

    /// <summary>
    /// Renvoie le Transform du XR Origin VR.
    /// Cherche dans les objets ACTIFS ET INACTIFS car sur le device AR,
    /// le VR XR Origin est désactivé par XRRigSwitcher.
    /// Priorité : champ Inspector → recherche par nom ("vr").
    /// </summary>
    private Transform ObtenirVROrigin()
    {
        if (_vrXROrigin != null)
            return _vrXROrigin;

        return TrouverXROriginParNom("vr");
    }

    /// <summary>
    /// Renvoie le Transform du XR Origin AR (Mobile AR).
    /// Cherche dans les objets ACTIFS ET INACTIFS car sur le device VR,
    /// le AR XR Origin est désactivé par XRRigSwitcher.
    /// Priorité : champ Inspector → recherche par nom ("ar").
    /// </summary>
    private Transform ObtenirAROrigin()
    {
        if (_arXROrigin != null)
            return _arXROrigin;
    
        var arPlayer = GameObject.FindWithTag("ARPlayer");
        if (arPlayer != null)
        {
            Debug.Log($"[TempleSceneManager] XR Origin AR auto-détecté via tag 'ARPlayer' : '{arPlayer.name}'");
            return arPlayer.transform;
        }
    
        Debug.LogWarning("[TempleSceneManager] Aucun GameObject avec le tag 'ARPlayer' trouvé. " +
                         "Assigne _arXROrigin dans l'Inspector ou vérifie le tag.");
        return null;
    }

    /// <summary>
    /// Parcourt tous les XROrigin de la scène (actifs et inactifs) et retourne
    /// le premier dont le nom contient <paramref name="nomPartiel"/> (insensible à la casse).
    /// </summary>
    private Transform TrouverXROriginParNom(string nomPartiel)
    {
        var allOrigins = FindObjectsByType<XROrigin>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var o in allOrigins)
        {
            if (o.name.IndexOf(nomPartiel, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log($"[TempleSceneManager] XR Origin '{nomPartiel}' auto-détecté : '{o.name}'");
                return o.transform;
            }
        }

        Debug.LogWarning($"[TempleSceneManager] Aucun XR Origin contenant '{nomPartiel}' trouvé. " +
                         "Assigne le champ dans l'Inspector ou vérifie le nom du GameObject.");
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
    // ── Locomotion VR ─────────────────────────────────────────────────────────

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
