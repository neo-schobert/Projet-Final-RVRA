using UnityEngine;

/// <summary>
/// Singleton DontDestroyOnLoad — mémorise la calibration et fournit les positions/rotations
/// de spawn dans TempleScene.
///
/// DESIGN :
///   arCam.transform.localPosition = position physique ARCore en mètres réels,
///   relative à Camera Offset (parent direct). Indépendante du XR Origin.
///   → Même en vue aérienne (XR Origin repositionné), localPosition reste correct.
///
///   Formule position :
///   ARSpawnPosition = VRSpawnRotation * _xrOriginRotCurrent * (localPos_now - localPos_calib)
///     - localPos_now      : position ARCore actuelle (mètres physiques)
///     - localPos_calib    : position ARCore au moment de la calibration
///     - _xrOriginRotCurrent : rotation XR Origin post-alignement (local → monde Scene.unity)
///     - VRSpawnRotation   : monde Scene.unity → espace TempleScene (VR at 0,0,0 looking +X)
///
///   Formule rotation AR :
///   ARSpawnRotation = VRSpawnRotation * deltaYaw
///     - deltaYaw : rotation entre le yaw AR au moment de la calibration
///                  et le yaw AR courant (caméra world), en espace TempleScene.
///     → si l'AR player tourne physiquement après calibration, ARSpawnRotation se met à jour.
/// </summary>
public class CalibrationPersistence : MonoBehaviour
{
    public static CalibrationPersistence Instance { get; private set; }
    public static bool HasData { get; private set; }

    // ── Données de calibration (figées à SaveCalibration) ──────────────────
    private static float      _vrYawAtCalib           = 0f;
    private static Quaternion _xrOriginRotCurrent     = Quaternion.identity; // mise à jour chaque frame
    private static Vector3    _camLocalPosAtCalib     = Vector3.zero;

    // ── Rotation caméra AR (mise à jour chaque frame) ───────────────────────
    // yaw world de arCam.transform au moment de la calibration → référence delta
    private static float      _arCamYawAtCalib        = 0f;
    // yaw world de arCam.transform courant → mis à jour via UpdateARRotation()
    private static float      _arCamYawCurrent        = 0f;

    // ── Spawn AR position ───────────────────────────────────────────────────
    private static Vector3    _arPhysicalDisplacement = Vector3.zero;

    // ── Position/rotation AR figées juste avant le passage en TempleScene ──
    // Synchronisées via RPC vers VR dans ARProximityCalibration.OnLancerJeuButtonPressed.
    // Sur VR, _arPhysicalDisplacement reste Vector3.zero (UpdateAR n'est jamais appelé)
    // → sans ces champs, VR placerait AR à 10 cm seulement et mal orienterait le gameboard.
    private static Vector3    _finalARSpawnPos  = Vector3.zero;
    private static Quaternion _finalARSpawnRot  = Quaternion.identity;
    private static bool       _hasFinalARSpawn  = false;

    // ── API spawn AR finale (pour TempleScene) ──────────────────────────────
    /// <summary>
    /// Position AR à utiliser dans TempleScene.
    /// Retourne la valeur synchronisée (SaveFinalARSpawn) si disponible,
    /// sinon ARSpawnPositionDirect (correct sur device AR, ~10 cm sur VR).
    /// </summary>
    public static Vector3 SpawnARPosition
        => _hasFinalARSpawn ? _finalARSpawnPos : ARSpawnPositionDirect;

    /// <summary>
    /// Rotation AR à utiliser dans TempleScene.
    /// Retourne la valeur synchronisée (SaveFinalARSpawn) si disponible,
    /// sinon ARSpawnRotationDirect (correct sur device AR, rotation calibration sur VR).
    /// </summary>
    public static Quaternion SpawnARRotation
        => _hasFinalARSpawn ? _finalARSpawnRot : ARSpawnRotationDirect;

    // ── API spawn VR ────────────────────────────────────────────────────────
    public static Vector3    VRSpawnPosition => Vector3.zero;

    /// <summary>
    /// Rotation VR avec remapping : aligne la direction physique VR→AR sur l'axe +X virtuel.
    /// Utilisé par ARSpawnRotation/ARPhysicalDisplacement (calcul interne).
    /// </summary>
    public static Quaternion VRSpawnRotation
    {
        get
        {
            if (!HasData) return Quaternion.identity;
            Vector3 vrForward = Quaternion.Euler(0f, _vrYawAtCalib, 0f) * Vector3.forward;
            float   angle     = Vector3.SignedAngle(vrForward, Vector3.right, Vector3.up);
            return Quaternion.AngleAxis(angle, Vector3.up);
        }
    }

    /// <summary>
    /// Rotation VR mapping DIRECT physique→virtuel.
    /// Le joueur VR est placé avec sa vraie rotation physique (yaw capturé à la calibration).
    /// Le gameboard s'aligne ensuite sur l'axe VR→AR réel.
    /// </summary>
    public static Quaternion VRSpawnRotationDirect
    {
        get
        {
            if (!HasData) return Quaternion.identity;
            return Quaternion.Euler(0f, _vrYawAtCalib, 0f);
        }
    }

    // ── API spawn AR ────────────────────────────────────────────────────────
    public static Vector3 ARSpawnPosition
    {
        get
        {
            return new Vector3(_arPhysicalDisplacement.x, 0f, 0f);
        }
    }

    /// <summary>
    /// Position AR mapping DIRECT physique→virtuel.
    /// Donne l'offset XZ de la caméra AR par rapport au joueur VR, en espace monde physique.
    ///
    /// Formule exacte (valide parce que AlignerARSurVR compense camLocalPosAtCalib) :
    ///   ARSpawnPositionDirect = rawDisp + calibOffset
    ///   rawDisp     = _xrOriginRotCurrent * (camLocalNow − camLocalAtCalib)   [delta physique]
    ///   calibOffset = vrForward * 0.1m                                         [offset calib initial]
    ///
    /// POURQUOI ça marche : AlignerARSurVR place le XR Origin à
    ///   (vrPos + vrForward*0.1) − XROriginRot * camLocalAtCalib
    /// → la caméra monde = vrPos + vrForward*0.1 à la calibration.
    /// → rawDisp + calibOffset = déplacement physique AR depuis VR, sans erreur.
    ///
    /// Sur VR device : _arPhysicalDisplacement = 0 (UpdateAR jamais appelé sur VR)
    ///   → utiliser SpawnARPosition qui prend la valeur synchronisée via SaveFinalARSpawn.
    /// </summary>
    public static Vector3 ARSpawnPositionDirect
    {
        get
        {
            if (!HasData) return new Vector3(0f, 0f, 2f); // fallback test : 2 m devant

            // rawDisp = déplacement physique depuis la calibration, en espace monde
            Vector3 rawDisp = Quaternion.Inverse(VRSpawnRotation) * _arPhysicalDisplacement;
            rawDisp.y = 0f;

            // calibOffset = offset caméra→VR à la calibration (10 cm devant VR)
            // Valide car AlignerARSurVR a placé la CAMÉRA (pas le rig) à vrPos+10cm.
            Vector3 calibOffset = Quaternion.Euler(0f, _vrYawAtCalib, 0f) * new Vector3(0f, 0f, 0.1f);

            return rawDisp + calibOffset;
        }
    }

    /// <summary>
    /// Rotation du joueur AR dans TempleScene, mise à jour chaque frame.
    ///
    /// Formule : VRSpawnRotation * Euler(deltaYaw) * _xrOriginRotCurrent
    ///
    /// ORDRE CRITIQUE :
    ///   _arCamYawCurrent / _arCamYawAtCalib sont des angles en ESPACE MONDE Unity
    ///   (arCam.transform.eulerAngles.y = yaw absolu dans le monde physique).
    ///   → deltaYaw est une rotation monde.
    ///
    ///   En quaternion : Q_monde * Q_local
    ///   • côté GAUCHE = espace monde → Euler(deltaYaw) doit être à GAUCHE de _xrOriginRotCurrent.
    ///   • côté DROIT  = espace local du XR Origin (ARCore).
    ///
    ///   BUG précédent : _xrOriginRotCurrent * Euler(deltaYaw)
    ///   → appliquait deltaYaw en espace ARCore local (côté droit).
    ///   → quand AR tournait physiquement, la rotation spawn était fausse
    ///     d'un angle proportionnel à l'écart entre l'orientation ARCore et le monde.
    /// </summary>
    public static Quaternion ARSpawnRotation
    {
        get
        {
            if (!HasData) return Quaternion.identity;

            float      deltaYaw = Mathf.DeltaAngle(_arCamYawAtCalib, _arCamYawCurrent);
            Quaternion arRot    = Quaternion.Euler(0f, deltaYaw, 0f) * _xrOriginRotCurrent;
            return VRSpawnRotation * arRot;
        }
    }

    /// <summary>
    /// Rotation AR mapping DIRECT physique→virtuel.
    /// Annule VRSpawnRotation pour retrouver la rotation physique réelle de l'AR.
    /// </summary>
    public static Quaternion ARSpawnRotationDirect
    {
        get
        {
            if (!HasData) return Quaternion.identity;
            // ARSpawnRotation = VRSpawnRotation * arRot → annuler VRSpawnRotation gauche
            return Quaternion.Inverse(VRSpawnRotation) * ARSpawnRotation;
        }
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── API publique ────────────────────────────────────────────────────────

    /// <summary>
    /// Appelé une fois juste après AlignerARSurVR.
    /// </summary>
    /// <param name="vrYaw">vrTarget.eulerAngles.y au moment de la calibration.</param>
    /// <param name="xrOriginRot">_arXROrigin.rotation APRÈS alignement.</param>
    /// <param name="camLocalPosAtCalib">arCam.transform.localPosition APRÈS alignement.</param>
    /// <param name="arCamYaw">arCam.transform.eulerAngles.y APRÈS alignement (yaw world caméra AR).</param>
    public static void SaveCalibration(float vrYaw, Quaternion xrOriginRot,
                                       Vector3 camLocalPosAtCalib, float arCamYaw)
    {
        _vrYawAtCalib           = vrYaw;
        _xrOriginRotCurrent     = xrOriginRot;
        _camLocalPosAtCalib     = camLocalPosAtCalib;
        _arCamYawAtCalib        = arCamYaw;
        _arCamYawCurrent        = arCamYaw;   // pas de delta au départ
        _arPhysicalDisplacement = Vector3.zero;
        _hasFinalARSpawn        = false;   // une re-calibration invalide le spawn figé précédent
        HasData                 = true;

        Debug.Log($"[CalibrationPersistence] ✓ Calibration — " +
                  $"vrYaw={vrYaw:F1}° | xrOriginRot={xrOriginRot.eulerAngles:F1}° | " +
                  $"camLocalAtCalib={camLocalPosAtCalib:F3} | arCamYaw={arCamYaw:F1}°");
    }

    /// <summary>
    /// Appelé juste avant le passage en TempleScene (sur device AR).
    /// Fige la position et rotation AR du moment pour que TempleSceneManager
    /// les utilise correctement sur les DEUX devices après sync réseau.
    /// </summary>
    public static void SaveFinalARSpawn(Vector3 position, Quaternion rotation)
    {
        _finalARSpawnPos = position;
        _finalARSpawnRot = rotation;
        _hasFinalARSpawn = true;
        Debug.Log($"[CalibrationPersistence] ✓ FinalARSpawn — pos={position:F3} | rot={rotation.eulerAngles:F1}°");
    }

    /// <summary>
    /// Mis à jour chaque frame par ARProximityCalibration.
    /// camLocalPosCurrent = arCam.transform.localPosition (position ARCore physique actuelle).
    /// arCamYawCurrent    = arCam.transform.eulerAngles.y (yaw world caméra AR actuel).
    /// </summary>
    public static void UpdateAR(Vector3 camLocalPosCurrent, float arCamYawCurrent)
    {
        if (!HasData) return;

        // ── Position ────────────────────────────────────────────────────────
        Vector3 localDelta = camLocalPosCurrent - _camLocalPosAtCalib;
        localDelta.y = 0f;

        // localDelta est dans l'espace local du XR Origin (espace ARCore).
        // _xrOriginRotCurrent le convertit en espace monde Scene.unity.
        // VRSpawnRotation le convertit en espace TempleScene.
        _arPhysicalDisplacement = VRSpawnRotation * (_xrOriginRotCurrent * localDelta);

        // ── Rotation ────────────────────────────────────────────────────────
        _arCamYawCurrent = arCamYawCurrent;

        Debug.Log($"[CalibPersist] localDelta={localDelta:F3} → ARSpawnPos={_arPhysicalDisplacement:F3} " +
                  $"| arYaw={arCamYawCurrent:F1}° (delta={Mathf.DeltaAngle(_arCamYawAtCalib, arCamYawCurrent):F1}°)");
    }
}