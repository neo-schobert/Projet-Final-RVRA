using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Système de calibration AR — sans image imprimée, sans action VR.
///
/// PRINCIPE :
///   Le joueur AR colle son téléphone contre le casque du joueur VR.
///   À cet instant les deux caméras sont au même point physique.
///   Il appuie sur le bouton "Calibration" (onglet top-right).
///   Le script :
///     1. Capture les positions physiques AR et VR (avant tout alignement).
///     2. Sauvegarde l'offset dans CalibrationPersistence (utilisé pour le spawn TempleScene).
///     3. Aligne le XR Origin AR sur le XR Origin VR dans l'espace virtuel partagé.
///
/// MATHÉMATIQUE DE L'ALIGNEMENT (étape 3) :
///   1. Rotation   : yaw AR XROrigin += DeltaAngle(yaw_AR_camera, yaw_VR_camera + 180°)
///                   → place le joueur AR FACE AU joueur VR (face à face), pas dans le même sens.
///                   Cibler yaw_VR sans +180° inverserait les axes → recul AR = AR va derrière VR.
///                   Rotation AVANT translation (pivot = XR Origin, pas caméra).
///   2. Translation: AR XROrigin.position = VR position + 10 cm (direction de regard VR)
///                   On place le RIG directement — pas de compensation offset caméra.
///
/// SETUP dans la scène :
///   A. Ce script est sur le GameObject "Calibration Manager".
///   B. _arXROrigin → XR Origin (Mobile AR) Transform.
///   C. _vrXROrigin → XR Origin (PC VR) Transform, assigné depuis l'Inspector.
///   D. Laisser _buttonPanel / _feedbackPanel vides : l'UI est créée automatiquement.
///      OU assigner des GameObjects personnalisés depuis l'Inspector.
/// </summary>
public class ARProximityCalibration : MonoBehaviour
{
    [Header("XR — obligatoire")]
    [Tooltip("Transform du XR Origin (Mobile AR). Auto-détecté si non assigné.")]
    [SerializeField] private Transform _arXROrigin;

    [Tooltip("Transform du XR Origin (PC VR). À assigner depuis l'Inspector.")]
    [SerializeField] private Transform _vrXROrigin;

    [Tooltip("GameObject 'StartGameMessage' présent dans la scène. " +
             "Glisse-le ici depuis l'Inspector — il sera activé directement quand 'Lancer le jeu' est pressé. " +
             "Fonctionne en éditeur (les deux joueurs sur la même machine) ET sur device (même scène partagée).")]
    [SerializeField] private GameObject _vrStartGameMessage;

    [Tooltip("Transform de la caméra VR (enfant du XR Origin VR). " +
             "OBLIGATOIRE pour un alignement précis : la caméra est à hauteur des yeux (~1.7 m), " +
             "pas au niveau du sol comme le XR Origin. À assigner depuis l'Inspector.")]
    [SerializeField] private Transform _vrCamera;

    [Header("UI — optionnel (créée automatiquement si vide)")]
    [Tooltip("Panel contenant le bouton Calibration (top-right). Créé auto si null.")]
    [SerializeField] private GameObject _buttonPanel;

    [Tooltip("Panel de feedback '✓ Calibré' affiché brièvement. Créé auto si null.")]
    [SerializeField] private GameObject _feedbackPanel;

    [Tooltip("Texte du feedback (auto-détecté depuis _feedbackPanel si null).")]
    [SerializeField] private TextMeshProUGUI _feedbackText;

    [Tooltip("Durée d'affichage du feedback (secondes).")]
    [SerializeField] private float _feedbackDuration = 3f;

    [Header("Vue aérienne")]
    [Tooltip("Hauteur caméra AR au-dessus du centre de la map (unités virtuelles). " +
             "La map fait 500 unités × 0.01 scale → ~5 m perçus.")]
    [SerializeField] private float _aerialHeight = 75f;

    [Tooltip("Facteur d'échelle appliqué au XR Origin AR pour la vue aérienne. " +
             "0.01 → 500 unités virtuelles ≈ 5 m perçus (map entière visible à ses pieds).")]
    [SerializeField] private float _mapScale = 0.01f;

    [Tooltip("Rayon de la zone de jeu physique en mètres (ex : 2.5 pour 5 m × 5 m). " +
             "Détermine le remapping : 1 m physique = map_half_extent / physical_radius unités virtuelles. " +
             "Avec 2.5 m et une demi-map de 250 unités → 1 m physique = 100 unités.")]
    [SerializeField] private float _physicalPlayRadius = 2.5f;

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>True après une première calibration réussie.</summary>
    public static bool IsCalibrated { get; private set; }

    private Coroutine _feedbackRoutine;

    // Créé à runtime dans CreerUIAutomatique(). Jamais assigné depuis l'Inspector.
    // Reste null si _buttonPanel était déjà assigné en Inspector (CreerUIAutomatique non appelée).
    private GameObject _lancerJeuPanel;

    // ── Vue aérienne : remapping physique → virtuel ────────────────────────────
    // Actif après OnLancerJeuButtonPressed. Update() repositionne l'XR Origin
    // chaque frame pour que le déplacement physique couvre toute la map.
    private bool    _aerialViewActive  = false;
    private float   _remapFactor       = 100f;          // unités virtuelles par mètre physique
    private Vector3 _camLocalAtLaunch  = Vector3.zero;  // localPos ARCore au moment de "Lancer le jeu"
                                                        // → seul le DELTA depuis ce point est remappé
                                                        // → le joueur est centré en (33,aerial,94) au départ

    // ── Tracking physique continu ───────────────────────────────────────────────
    // Actif dès la première calibration réussie.
    // Permet de calculer la position physique AR dans l'espace virtuel calibré
    // À TOUT MOMENT — y compris pendant la vue aérienne où Camera.main.position
    // est remappée et n'est plus utilisable comme position physique.
    //
    // Principe :
    //   arCam.transform.localPosition est mis à jour par ARCore indépendamment
    //   de la scale/position du XR Origin → toujours en mètres physiques.
    //   Position physique AR = _arPhysPosAtCalib + _xrOriginRotAtCalib * localDelta
    //   Position physique VR = PlayerSetup.transform.position (synchro réseau)
    private bool       _trackingActive     = false;
    private Vector3    _arPhysPosAtCalib   = Vector3.zero;       // position partagée au calibrage
    private Vector3    _camLocalPosAtCalib = Vector3.zero;        // localPos caméra au calibrage
    private Quaternion _xrOriginRotAtCalib = Quaternion.identity; // rotation XR Origin post-calibrage
    private PlayerSetup _remoteVRPlayer    = null;                // joueur VR distant (réseau)

    /// <summary>
    /// Cherche le PlayerSetup du joueur VR distant — celui dont le joueur AR n'est PAS propriétaire.
    /// IMPORTANT : FindAnyObjectByType sans filtre peut retourner le propre PlayerSetup de l'AR
    /// (si le prefab PlayerAR en a un), ce qui donnerait une position erronée (la sienne).
    /// On filtre explicitement sur !IsOwner pour garantir de trouver le joueur VR.
    /// </summary>
    private PlayerSetup TrouverJoueurVRDistant()
    {
        foreach (var ps in FindObjectsByType<PlayerSetup>(FindObjectsSortMode.None))
        {
            if (!ps.IsOwner)
                return ps;
        }
        return null;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        // Ce script ne fait rien sur le casque VR
        if (XRRigSwitcher.IsVRMode)
        {
            enabled = false;
            return;
        }

        // Auto-détection du XR Origin AR (actif après XRRigSwitcher.Awake)
        if (_arXROrigin == null)
        {
            Unity.XR.CoreUtils.XROrigin found =
                FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (found != null)
                _arXROrigin = found.transform;
            else
                Debug.LogWarning("[ARProximityCalibration] XR Origin AR introuvable. " +
                                 "Assigne _arXROrigin dans l'Inspector.");
        }

        if (_vrXROrigin == null)
            Debug.LogWarning("[ARProximityCalibration] _vrXROrigin non assigné dans l'Inspector. " +
                             "Glisse le XR Origin VR depuis la scène.");

        // Masquer l'ancienne UI de calibration image si elle existe encore
        GameObject oldSearching = GameObject.Find("SearchingPanel");
        if (oldSearching != null) oldSearching.SetActive(false);

        // Créer l'UI bouton si rien n'est assigné en Inspector
        if (_buttonPanel == null)
            CreerUIAutomatique();
    }

    // ── Update : tracking physique continu + remapping vue aérienne ──────────

    private void Update()
    {
        Camera arCam = Camera.main;
        if (arCam == null) return;

        // ── A. Tracking physique continu ──────────────────────────────────────
        // Actif dès la calibration, pendant TOUTE la session (vue normale ET aérienne).
        // Met à jour CalibrationPersistence avec la distance VR↔AR en temps réel,
        // pour que le spawn dans TempleScene reflète toujours les positions actuelles.
        if (_trackingActive)
            MettreAJourOffsetPhysique(arCam);

        // ── B. Remapping vue aérienne ─────────────────────────────────────────
        // Actif uniquement après "Lancer le jeu". Repositionne le XR Origin
        // pour que le déplacement physique (5 m) couvre toute la map (500 u).
        //
        //   cam_world = origin_world + cam_local * scale
        //   → origin_world = desired_cam_world - cam_local * scale
        if (!_aerialViewActive || _arXROrigin == null) return;

        // DELTA depuis le point de lancement (= 0,0,0 au démarrage de la vue aérienne).
        // On soustrait _camLocalAtLaunch pour que la position physique au moment du
        // clic sur "Lancer le jeu" corresponde exactement au centre de la map (33,aerial,94).
        // Sans ce delta, la position absolue ARCore (ex: 0.5 m) × remapFactor (100)
        // décalerait le joueur de 50 unités dès le premier frame.
        Vector3 camLocal = arCam.transform.localPosition - _camLocalAtLaunch;
        float   s        = _mapScale;

        // Convertir le déplacement physique (espace local ARCore) en espace monde
        // en tenant compte de la rotation du XR Origin post-calibration.
        //
        // POURQUOI : après calibration, _arXROrigin est tourné pour aligner les espaces
        // VR et AR. Sans cette rotation, un pas physique vers l'avant peut correspondre
        // à une direction arbitraire sur la map selon l'angle de calibration
        // (d'où l'impression que le déplacement est inversé ou de travers).
        //
        // Formule : cam_world = origin_pos + origin_rot * (s * camLocal)
        // On veut  : cam_world = centre_map + physWorld * remapFactor
        // Donc     : origin_pos = centre_map + physWorld * (remapFactor - s)
        // avec physWorld = origin_rot * camLocal  (local → monde)
        Vector3 physWorld = _arXROrigin.rotation * camLocal;

        // XZ : déplacement physique remappé sur toute la map (100 u/m par défaut).
        // Y  : lever/baisser le téléphone ajuste l'altitude (zoom vertical).
        _arXROrigin.position = new Vector3(
            33f           + physWorld.x * (_remapFactor - s),
            _aerialHeight + physWorld.y * (_remapFactor - s),
            94f           + physWorld.z * (_remapFactor - s)
        );
    }

    /// <summary>
    /// Recalcule la position physique AR dans l'espace virtuel calibré et met à jour
    /// CalibrationPersistence en silence (pas de log flood).
    ///
    /// Pourquoi localPosition et pas Camera.main.position ?
    ///   En vue aérienne, Camera.main.position est remappée à ~75 m d'altitude.
    ///   arCam.transform.localPosition est la position de suivi ARCore relative
    ///   à Camera Offset (parent direct) — toujours en mètres physiques réels,
    ///   indépendante de la scale/position du XR Origin.
    ///
    /// Pourquoi arCam.transform.eulerAngles.y et pas _arXROrigin.rotation ?
    ///   ARCore ne tourne pas le XR Origin quand le joueur pivote physiquement —
    ///   seule la caméra (enfant de Camera Offset) tourne. On capture donc le yaw
    ///   directement sur la caméra pour détecter les rotations post-calibration.
    ///
    /// Formule position :
    ///   AR_phys = _arPhysPosAtCalib + _xrOriginRotAtCalib × (localPos_now − localPos_calib)
    ///   VR_phys = PlayerSetup.transform.position (synchronisé réseau, = caméra VR monde)
    /// </summary>
    private void MettreAJourOffsetPhysique(Camera arCam)
    {
        if (CalibrationPersistence.Instance == null) return;

        CalibrationPersistence.UpdateAR(
            arCam.transform.localPosition,
            arCam.transform.eulerAngles.y   // yaw world caméra AR courant
        );
    }

    // ── Calibration (appelée par le bouton onClick) ────────────────────────────

    /// <summary>
    /// Appelée par le bouton "Calibration" sur l'interface AR.
    /// Le joueur AR doit tenir son téléphone CONTRE le casque VR au moment de l'appui.
    /// Capture l'offset physique AR→VR, le sauvegarde, puis aligne le XR Origin AR.
    /// Après calibration réussie, affiche le bouton "Lancer le jeu".
    /// </summary>
    public void OnCalibrationButtonPressed()
    {
        if (_arXROrigin == null)
        {
            AfficherFeedback("❌ XR Origin AR introuvable !");
            return;
        }

        Camera arCam = Camera.main;
        if (arCam == null)
        {
            AfficherFeedback("❌ Caméra AR introuvable !");
            return;
        }

        if (_vrXROrigin == null)
        {
            AfficherFeedback("❌ XR Origin VR non assigné !");
            Debug.LogWarning("[ARProximityCalibration] _vrXROrigin est null. " +
                             "Assigne le XR Origin VR dans l'Inspector.");
            return;
        }

        // ── Étape 1 : capturer les positions physiques AVANT alignement ──────
        // IMPORTANT : _vrCamera et _vrXROrigin sont des GameObjects LOCAUX à la scène.
        // Sur l'appareil AR, le rig VR est inactif → ces transforms sont à (0,0,0).
        // La vraie position du joueur VR = PlayerSetup du joueur distant (non-owner),
        // synchronisée chaque frame via ClientNetworkTransform.
        Vector3 arPhysPos = arCam.transform.position;

        // Recherche du joueur VR distant — OBLIGATOIREMENT le non-owner.
        // FindAnyObjectByType sans filtre peut retourner notre propre PlayerSetup → position erronée.
        if (_remoteVRPlayer == null)
            _remoteVRPlayer = TrouverJoueurVRDistant();

        if (_remoteVRPlayer == null)
        {
            AfficherFeedback("❌ Joueur VR non connecté !\nAttends que le VR rejoigne la session.");
            Debug.LogWarning("[ARProximityCalibration] Calibration annulée : aucun joueur VR distant trouvé. " +
                             "Assure-toi que le joueur VR est connecté et que son PlayerSetup est spawné " +
                             "avant de calibrer.");
            return;
        }

        Transform vrTarget = _remoteVRPlayer.transform;
        Debug.Log($"[ARProximityCalibration] Cible VR = PlayerSetup distant (non-owner) → " +
                  $"pos={vrTarget.position:F3}  yaw={vrTarget.eulerAngles.y:F1}°");

        // ── Étape 2 : capturer le yaw VR AVANT alignement ────────────────────
        // Nécessaire pour VRSpawnRotation (mapper direction physique VR → +X virtuel).
        float vrYaw = vrTarget.eulerAngles.y;

        // ── Étape 3 : aligner le XR Origin AR sur la caméra VR ───────────────
        AlignerARSurVR(arCam, vrTarget);

        // ── Étape 4 : activer le tracking physique continu ───────────────────
        // Capturé POST-alignement → dans le système de coordonnées calibré.
        _arPhysPosAtCalib   = arCam.transform.position;
        _camLocalPosAtCalib = arCam.transform.localPosition;
        _xrOriginRotAtCalib = _arXROrigin.rotation;
        _trackingActive     = true;

        // ── Étape 5 : sauvegarder la calibration ─────────────────────────────
        // vrYaw + xrOriginRot + camLocalPos + arCamYaw suffisent.
        // Le déplacement AR (Vector3.zero au départ) et la rotation AR seront mis à jour
        // chaque frame dans MettreAJourOffsetPhysique via UpdateAR.
        // arCamYaw POST-alignement = référence de rotation pour calculer le delta
        // de rotation physique AR entre la calibration et le spawn dans TempleScene.
        CalibrationPersistence.SaveCalibration(
            vrYaw,
            _xrOriginRotAtCalib,
            arCam.transform.localPosition,
            arCam.transform.eulerAngles.y   // ← yaw caméra AR post-alignement
        );

        // ── Sync réseau → VR ─────────────────────────────────────────────────
        // CalibrationPersistence.SaveCalibration() vient d'être appelé sur ce device (AR).
        // Sur le device VR, HasData reste false sans sync réseau → TempleSceneManager
        // ne peut pas positionner les deux joueurs ni orienter le gameboard.
        // On utilise le NetworkBehaviour de _remoteVRPlayer pour diffuser via RPC.
        if (_remoteVRPlayer != null)
            _remoteVRPlayer.SyncCalibrationToAll(
                vrYaw,
                _xrOriginRotAtCalib,
                arCam.transform.localPosition,
                arCam.transform.eulerAngles.y
            );
        else
            Debug.LogWarning("[ARProximityCalibration] _remoteVRPlayer null — " +
                             "calibration NON synchronisée vers VR (HasData restera false sur VR).");

        IsCalibrated = true;
        AfficherFeedback("✓ Calibration réussie !");

        if (_lancerJeuPanel != null)
            _lancerJeuPanel.SetActive(true);

        Debug.Log($"[ARProximityCalibration] ✓ Calibration OK — " +
                  $"VR yaw={vrYaw:F1}°  AR origin rot={_xrOriginRotAtCalib.eulerAngles:F1}°  " +
                  $"AR cam yaw={arCam.transform.eulerAngles.y:F1}°");
    }

    // ── Vue aérienne (appelée par le bouton "Lancer le jeu") ──────────────────

    /// <summary>
    /// Téléporte le joueur AR en vue aérienne au-dessus du centre exact de la map.
    ///
    /// Dimensions map : X [-216, +282] largeur=498u  |  Z [-156, +344] profondeur=500u
    /// Centre          : X = (-216+282)/2 = 33        |  Z = (-156+344)/2 = 94
    /// Scale           : _mapScale (défaut 0.01) → 500u = 5 m perçus
    ///
    /// ORDRE STRICT (important) :
    ///   1. Désactiver CharacterController si présent (évite conflits de déplacement).
    ///   2. Appliquer le scale sur _arXROrigin EN PREMIER — avant tout calcul de position.
    ///      Après scale, l'offset entre _arXROrigin et Camera.main est lui aussi scalé.
    ///   3. Lire l'offset post-scale : offsetCamToOrigin = _arXROrigin.pos - Camera.main.pos
    ///      Positionner : _arXROrigin.pos = (33, _aerialHeight, 94) + offsetCamToOrigin
    ///   4. Cacher _lancerJeuPanel.
    ///   5. Réactiver CharacterController.
    ///   6. Logger la position finale.
    /// </summary>
    public void OnLancerJeuButtonPressed()
    {
        if (_arXROrigin == null)
        {
            Debug.LogError("[ARProximityCalibration] OnLancerJeuButtonPressed : _arXROrigin est null !");
            return;
        }

        Camera arCam = Camera.main;
        if (arCam == null)
        {
            Debug.LogError("[ARProximityCalibration] OnLancerJeuButtonPressed : Camera.main est null !");
            return;
        }

        // ── 0. Figer et synchroniser la position AR finale vers tous les devices ─
        // Fait ICI, avant que la vue aérienne ne déplace le XR Origin et que
        // CalibrationPersistence.UpdateAR ne soit plus appelé (fin de MettreAJourOffsetPhysique).
        // Sur VR, _arPhysicalDisplacement = Vector3.zero (UpdateAR n'est jamais appelé) →
        // sans ce sync, VR placerait AR à 10 cm et orienterait mal le gameboard.
        {
            Camera arCamFinal = Camera.main;
            if (arCamFinal != null)
                MettreAJourOffsetPhysique(arCamFinal);   // garantit la valeur la plus fraîche

            Vector3    finalPos = CalibrationPersistence.ARSpawnPositionDirect;
            Quaternion finalRot = CalibrationPersistence.ARSpawnRotationDirect;
            CalibrationPersistence.SaveFinalARSpawn(finalPos, finalRot);

            if (_remoteVRPlayer != null)
                _remoteVRPlayer.SyncFinalARSpawnToAll(finalPos, finalRot);
            else
                Debug.LogWarning("[ARProximityCalibration] _remoteVRPlayer null — " +
                                 "FinalARSpawn NON synchronisé vers VR (gameboard sera mal orienté).");

            Debug.Log($"[ARProximityCalibration] FinalARSpawn envoyé — pos={finalPos:F3} | rot={finalRot.eulerAngles:F1}°");
        }

        // ── 1. Désactiver CharacterController avant tout mouvement ───────────
        CharacterController cc = _arXROrigin.GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;

        // ── 2. Appliquer le scale (EN PREMIER — avant tout calcul de position) ─
        // Après scale, l'offset entre _arXROrigin et Camera.main est scalé.
        _arXROrigin.localScale = Vector3.one * _mapScale;

        // ── 3. Positionner pour centrer la caméra au-dessus du centre de la map ─
        // On lit l'offset POST-scale (il a changé car localScale modifie les positions enfants).
        // Vector3 offsetCamToOrigin = _arXROrigin.position - arCam.transform.position;
        _arXROrigin.position = new Vector3(33f, _aerialHeight, 94f);

        // ── 4. Activer le remapping continu physique → virtuel ───────────────
        // Chaque mètre physique parcouru = _remapFactor unités virtuelles.
        // La demi-map fait ~250 unités ; la zone de jeu physique = _physicalPlayRadius.
        // Facteur = 250 / 2.5 = 100 par défaut (5 m phys couvrent 500 unités virtuelles).
        _remapFactor      = 250f / Mathf.Max(0.1f, _physicalPlayRadius);

        // Capturer la localPosition ARCore au moment du lancement.
        // Update() n'utilisera que le DELTA depuis ce point → joueur centré en (33,aerial,94)
        // même s'il s'est déplacé physiquement avant de cliquer sur "Lancer le jeu".
        _camLocalAtLaunch = arCam.transform.localPosition;
        _aerialViewActive = true;

        Debug.Log($"[ARProximityCalibration] camLocalAtLaunch={_camLocalAtLaunch:F3} (référence vue aérienne).");

        // ── 5. Cacher le bouton ───────────────────────────────────────────────
        if (_lancerJeuPanel != null)
            _lancerJeuPanel.SetActive(false);

        // ── 6. Réactiver CharacterController ─────────────────────────────────
        if (cc != null) cc.enabled = true;

        // ── 7. Log ────────────────────────────────────────────────────────────
        Debug.Log($"[ARProximityCalibration] 🌍 Vue aérienne — " +
                  $"scale={_arXROrigin.localScale.x:F4}  " +
                  $"remapFactor={_remapFactor:F1}  " +
                  $"physRadius={_physicalPlayRadius:F1}m  " +
                  $"(Update() repositionne l'origine chaque frame)");
        _vrXROrigin.position = new Vector3(0, 0, 0); // aligner VR sur centre map aussi
        _vrXROrigin.rotation = Quaternion.Euler(0, 0, 0);
        // ── Message début de jeu ──────────────────────────────────────────────
        // Activation directe via la référence Inspector (fonctionne en éditeur
        // où AR et VR sont sur la même machine, et sur device si la scène est partagée).
        if (_vrStartGameMessage != null)
        {
            _vrStartGameMessage.SetActive(true);
            StartCoroutine(CacherStartGameMessage(5f));
            Debug.Log("[ARProximityCalibration] ✓ StartGameMessage activé directement.");
        }
    }

    // ── Mathématique d'alignement ─────────────────────────────────────────────

    /// <summary>
    /// Repositionne et réoriente le RIG AR (_arXROrigin) pour placer le joueur AR
    /// juste devant le joueur VR, face à lui.
    ///
    /// On déplace directement le XR Origin (le rig), pas la caméra.
    /// La caméra AR est un enfant du rig et suit naturellement.
    ///
    /// ORDRE : rotation AVANT translation.
    ///   La rotation pivote autour du XR Origin. En la faisant en premier,
    ///   le rig est déjà orienté correctement quand on lui assigne sa position finale.
    ///
    /// OFFSET 10 CM :
    ///   Le rig AR est placé à 10 cm dans la direction de regard du joueur VR,
    ///   pour qu'ils apparaissent face à face sans se superposer.
    /// </summary>
    /// <param name="vrTarget">PlayerSetup du joueur VR distant (non-owner).
    /// Sa position XZ et son yaw définissent la cible de placement du rig AR.</param>
    private void AlignerARSurVR(Camera arCam, Transform vrTarget)
    {
        // ── Étape 1 : aligner le yaw pour placement FACE À FACE ─────────────
        // OBJECTIF : après calibration, le joueur AR doit FAIRE FACE au joueur VR.
        //
        // ERREUR CLASSIQUE : cibler vrYaw fait que les deux joueurs regardent dans
        // la MÊME direction après calibration. Conséquence : quand le joueur AR recule
        // physiquement (loin du casque), le XR Origin tourné inverse le Z → AR se retrouve
        // DERRIÈRE le joueur VR au lieu d'être devant. C'est le bug "loin du joueur VR".
        //
        // CORRECT : cibler vrYaw + 180° pour que le joueur AR fasse face au joueur VR.
        // Exemple : VR face nord (0°), AR face sud (180°) → cible = 0°+180° = 180°
        //           deltYaw = DeltaAngle(180°, 180°) = 0° → pas de rotation
        //           XR Origin reste aligné avec le monde physique → physique nord = virtuel nord ✓
        //           Le joueur AR recule physiquement (nord) → avance virtuellement (nord) = devant VR ✓
        float arYaw   = arCam.transform.eulerAngles.y;
        float vrYaw   = vrTarget.eulerAngles.y;
        float deltYaw = Mathf.DeltaAngle(arYaw, vrYaw + 180f);  // Face à face : AR regarde VERS VR

        _arXROrigin.Rotate(0f, deltYaw, 0f, Space.World);

        // ── Étape 2 : positionner le RIG AR de sorte que la CAMÉRA soit à 10 cm devant VR ──
        // BUG PRÉCÉDENT : on plaçait le XR Origin à vrPos+10cm en supposant camLocalPos=(0,0,0).
        // En réalité, ARCore a accumulé un tracking depuis le début de session →
        // arCam.localPosition ≠ (0,0,0). Placer le rig sans compenser donne :
        //   camera_world = (vrPos+10cm) + XROriginRot * camLocalPos   ← décalé !
        // Ce décalage se propage dans ARSpawnPositionDirect (terme manquant = XROriginRot*camLocal)
        // et génère une erreur angulaire sur le gameboard (ex : 144° au lieu de 180°).
        //
        // CORRECTION : on cherche XROrigin.pos tel que camera_world = desiredCamPos.
        //   camera_world  = XROrigin.pos + XROrigin.rot * camLocalPos
        //   → XROrigin.pos = desiredCamPos - XROrigin.rot * camLocalPos
        //
        // Remarque : on n'annule que les composantes XZ (on préserve le Y du rig = niveau sol).
        //            XROrigin.rotation est DÉJÀ mis à jour par l'étape 1.
        Vector3 vrPos       = vrTarget.position;
        Vector3 vrForwardXZ = Quaternion.Euler(0f, vrYaw, 0f) * Vector3.forward;
        const float offsetDevant = 0.1f; // 10 cm devant le joueur VR

        // Position monde souhaitée pour la CAMÉRA (XZ uniquement)
        Vector3 desiredCamXZ = new Vector3(
            vrPos.x + vrForwardXZ.x * offsetDevant,
            _arXROrigin.position.y,          // Y du rig conservé (niveau sol)
            vrPos.z + vrForwardXZ.z * offsetDevant
        );

        // Compense la localPosition ARCore de la caméra (XZ seulement, le Y ne touche pas le plan jeu)
        Vector3 camLocalFlat = new Vector3(arCam.transform.localPosition.x, 0f, arCam.transform.localPosition.z);
        Vector3 camWorldOffsetXZ = _arXROrigin.rotation * camLocalFlat;  // local → monde (rotation de l'étape 1)

        _arXROrigin.position = new Vector3(
            desiredCamXZ.x - camWorldOffsetXZ.x,
            desiredCamXZ.y,                          // Y inchangé
            desiredCamXZ.z - camWorldOffsetXZ.z
        );

        Debug.Log($"[ARProximityCalibration] AlignerARSurVR — " +
                  $"vrPos={vrPos:F3}  vrYaw={vrYaw:F1}°  " +
                  $"camLocalFlat={camLocalFlat:F3}  camWorldOffset={camWorldOffsetXZ:F3}  " +
                  $"rig AR → {_arXROrigin.position:F3}  cam world≈{desiredCamXZ:F3} ({offsetDevant*100:F0} cm devant VR)");
    }

    // ── Feedback UI ───────────────────────────────────────────────────────────

    private void AfficherFeedback(string message)
    {
        if (_feedbackText != null)
            _feedbackText.text = message;

        if (_feedbackPanel != null)
            _feedbackPanel.SetActive(true);

        if (_feedbackRoutine != null)
            StopCoroutine(_feedbackRoutine);

        _feedbackRoutine = StartCoroutine(MasquerFeedback(_feedbackDuration));
    }

    private IEnumerator MasquerFeedback(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_feedbackPanel != null)
            _feedbackPanel.SetActive(false);
    }

    private IEnumerator CacherStartGameMessage(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_vrStartGameMessage != null)
            _vrStartGameMessage.SetActive(false);
    }

    // ── Création automatique de l'UI ─────────────────────────────────────────

    /// <summary>
    /// Crée une UI Screen Space Overlay minimale si _buttonPanel n'est pas assigné en Inspector.
    /// Panels créés :
    ///   • CalibrationButtonPanel  — top-right, toujours visible.
    ///   • CalibrationFeedbackPanel — top-center, caché par défaut, affiché après calibration.
    ///   • LancerJeuPanel          — centre écran, caché par défaut,
    ///                               affiché uniquement après calibration réussie.
    ///
    /// Positions descendues à -80 (vs -24 précédemment) pour rester visible
    /// sous la barre d'état système des téléphones AR.
    /// </summary>
    private void CreerUIAutomatique()
    {
        // ── Canvas principal ──────────────────────────────────────────────────
        GameObject canvasGO = new GameObject("AR_CalibrationCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Panel bouton Calibration (top-right) ──────────────────────────────
        // anchoredPosition.y = -80 (descendu vs -24) pour éviter la coupure en haut.
        GameObject panelGO = new GameObject("CalibrationButtonPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        Image panelImg         = panelGO.AddComponent<Image>();
        panelImg.color         = new Color(0f, 0f, 0f, 0.55f);
        panelImg.raycastTarget = true;

        RectTransform panelRT    = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(1f, 1f);
        panelRT.anchorMax        = new Vector2(1f, 1f);
        panelRT.pivot            = new Vector2(1f, 1f);
        panelRT.anchoredPosition = new Vector2(-24f, -80f);
        panelRT.sizeDelta        = new Vector2(220f, 70f);

        GameObject btnGO  = new GameObject("CalibrationButton");
        btnGO.transform.SetParent(panelGO.transform, false);

        Image btnImg      = btnGO.AddComponent<Image>();
        btnImg.color      = new Color(0.08f, 0.55f, 1f, 0.92f);

        RectTransform btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin     = Vector2.zero;
        btnRT.anchorMax     = Vector2.one;
        btnRT.offsetMin     = new Vector2(8f,  8f);
        btnRT.offsetMax     = new Vector2(-8f, -8f);

        Button btn        = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;

        ColorBlock cb       = btn.colors;
        cb.highlightedColor = new Color(0.2f, 0.7f, 1f, 1f);
        cb.pressedColor     = new Color(0.05f, 0.35f, 0.8f, 1f);
        btn.colors          = cb;

        btn.onClick.AddListener(OnCalibrationButtonPressed);

        GameObject lblGO = new GameObject("Label");
        lblGO.transform.SetParent(btnGO.transform, false);

        TextMeshProUGUI lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text            = "⊕  Calibration";
        lbl.fontSize        = 28f;
        lbl.fontStyle       = FontStyles.Bold;
        lbl.alignment       = TextAlignmentOptions.Center;
        lbl.color           = Color.white;

        RectTransform lblRT = lbl.GetComponent<RectTransform>();
        lblRT.anchorMin     = Vector2.zero;
        lblRT.anchorMax     = Vector2.one;
        lblRT.offsetMin     = Vector2.zero;
        lblRT.offsetMax     = Vector2.zero;

        _buttonPanel = panelGO;

        // ── Panel feedback (top-center) ───────────────────────────────────────
        // anchoredPosition.y = -80 (descendu vs -24) pour rester visible.
        GameObject fbGO = new GameObject("CalibrationFeedbackPanel");
        fbGO.transform.SetParent(canvasGO.transform, false);
        fbGO.SetActive(false);

        Image fbImg           = fbGO.AddComponent<Image>();
        fbImg.color           = new Color(0.06f, 0.55f, 0.22f, 0.88f);
        fbImg.raycastTarget   = false;

        RectTransform fbRT    = fbGO.GetComponent<RectTransform>();
        fbRT.anchorMin        = new Vector2(0.5f, 1f);
        fbRT.anchorMax        = new Vector2(0.5f, 1f);
        fbRT.pivot            = new Vector2(0.5f, 1f);
        fbRT.anchoredPosition = new Vector2(0f, -80f);
        fbRT.sizeDelta        = new Vector2(420f, 70f);

        GameObject ftGO = new GameObject("FeedbackText");
        ftGO.transform.SetParent(fbGO.transform, false);

        TextMeshProUGUI ft = ftGO.AddComponent<TextMeshProUGUI>();
        ft.text            = "✓ Calibration réussie !";
        ft.fontSize        = 26f;
        ft.alignment       = TextAlignmentOptions.Center;
        ft.color           = Color.white;
        ft.raycastTarget   = false;

        RectTransform ftRT = ft.GetComponent<RectTransform>();
        ftRT.anchorMin     = Vector2.zero;
        ftRT.anchorMax     = Vector2.one;
        ftRT.offsetMin     = new Vector2(12f, 4f);
        ftRT.offsetMax     = new Vector2(-12f, -4f);

        _feedbackPanel = fbGO;
        _feedbackText  = ft;

        // ── Panel "Lancer le jeu" (centre écran, caché par défaut) ───────────
        // Visible uniquement après calibration réussie (OnCalibrationButtonPressed).
        // Couleur orange/or — visuellement distinct du bouton Calibration (bleu).
        GameObject ljGO = new GameObject("LancerJeuPanel");
        ljGO.transform.SetParent(canvasGO.transform, false);
        ljGO.SetActive(false);

        Image ljImg         = ljGO.AddComponent<Image>();
        ljImg.color         = new Color(0f, 0f, 0f, 0.60f);
        ljImg.raycastTarget = true;

        RectTransform ljRT    = ljGO.GetComponent<RectTransform>();
        ljRT.anchorMin        = new Vector2(0.5f, 0.5f);
        ljRT.anchorMax        = new Vector2(0.5f, 0.5f);
        ljRT.pivot            = new Vector2(0.5f, 0.5f);
        ljRT.anchoredPosition = Vector2.zero;
        ljRT.sizeDelta        = new Vector2(300f, 100f);

        GameObject ljBtnGO  = new GameObject("LancerJeuButton");
        ljBtnGO.transform.SetParent(ljGO.transform, false);

        Image ljBtnImg      = ljBtnGO.AddComponent<Image>();
        ljBtnImg.color      = new Color(0.95f, 0.60f, 0.05f, 0.95f);

        RectTransform ljBtnRT = ljBtnGO.GetComponent<RectTransform>();
        ljBtnRT.anchorMin     = Vector2.zero;
        ljBtnRT.anchorMax     = Vector2.one;
        ljBtnRT.offsetMin     = new Vector2(10f, 10f);
        ljBtnRT.offsetMax     = new Vector2(-10f, -10f);

        Button ljBtn        = ljBtnGO.AddComponent<Button>();
        ljBtn.targetGraphic = ljBtnImg;

        ColorBlock ljCb       = ljBtn.colors;
        ljCb.highlightedColor = new Color(1f, 0.75f, 0.20f, 1f);
        ljCb.pressedColor     = new Color(0.75f, 0.42f, 0.02f, 1f);
        ljBtn.colors          = ljCb;

        ljBtn.onClick.AddListener(OnLancerJeuButtonPressed);

        GameObject ljLblGO = new GameObject("Label");
        ljLblGO.transform.SetParent(ljBtnGO.transform, false);

        TextMeshProUGUI ljLbl = ljLblGO.AddComponent<TextMeshProUGUI>();
        ljLbl.text            = "🌍  Lancer le jeu";
        ljLbl.fontSize        = 34f;
        ljLbl.fontStyle       = FontStyles.Bold;
        ljLbl.alignment       = TextAlignmentOptions.Center;
        ljLbl.color           = Color.white;

        RectTransform ljLblRT = ljLbl.GetComponent<RectTransform>();
        ljLblRT.anchorMin     = Vector2.zero;
        ljLblRT.anchorMax     = Vector2.one;
        ljLblRT.offsetMin     = Vector2.zero;
        ljLblRT.offsetMax     = Vector2.zero;

        _lancerJeuPanel = ljGO;

        Debug.Log("[ARProximityCalibration] UI créée automatiquement " +
                  "(CalibrationButton + FeedbackPanel + LancerJeuPanel).");
    }
}