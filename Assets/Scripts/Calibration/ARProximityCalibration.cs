using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Système de calibration AR + vidéo d'intro synchronisée en réseau.
///
/// FLUX COMPLET :
///   1. Le joueur AR calibre (bouton "Calibration") → aligne les espaces AR/VR.
///   2. Il presse "Lancer le jeu" → RequestLaunchIntroVideo() est appelé.
///   3. Un Rpc(SendTo.Everyone, InvokePermission.Everyone) diffuse le signal à TOUS les clients.
///   4. Chaque client joue la vidéo localement (pas de sync frames réseau).
///   5. À la fin de la vidéo → OnIntroVideoFinished() déclenche le gameplay.
///
/// VIDÉO :
///   • VR  : Canvas World Space positionné et orienté face à la caméra VR.
///   • AR  : Canvas Screen Space Overlay par-dessus tout le reste (sortingOrder 20).
///   • RenderTexture partagée entre les deux RawImage.
///   • AudioSource local sur ce GO pour la piste audio de la vidéo.
///
/// PRINCIPE CALIBRATION (inchangé) :
///   Le joueur AR colle son téléphone contre le casque du joueur VR.
///   → Les deux caméras sont au même point physique.
///   → On aligne le XR Origin AR sur l'espace VR (rotation face-à-face + translation).
///
/// SETUP dans la scène :
///   A. Ce script est sur un NetworkObject "Calibration Manager".
///   B. _arXROrigin → XR Origin (Mobile AR) Transform.
///   C. _vrXROrigin → XR Origin (PC VR) Transform.
///   D. _introClip  → glisse ta vidéo .mp4 (H.264, Transcode ✓).
///   E. _vrVideoPanel / _arVideoPanel → créés automatiquement si non assignés.
///   F. _vrCamera   → caméra enfant du XR Origin VR (OBLIGATOIRE pour alignement).
///   G. _vrStartGameMessage → GameObject activé à la fin de la vidéo.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class ARProximityCalibration : NetworkBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  INSPECTOR — XR
    // ═══════════════════════════════════════════════════════════════════════════

    [Header("XR — obligatoire")]
    [Tooltip("Transform du XR Origin (Mobile AR). Auto-détecté si non assigné.")]
    [SerializeField] private Transform _arXROrigin;

    [Tooltip("Transform du XR Origin (PC VR). À assigner depuis l'Inspector.")]
    [SerializeField] private Transform _vrXROrigin;

    [Tooltip("Transform de la caméra VR (enfant du XR Origin VR). " +
             "OBLIGATOIRE pour un alignement précis.")]
    [SerializeField] private Transform _vrCamera;

    [Tooltip("GameObject 'StartGameMessage' activé à la fin de la vidéo d'intro. " +
             "Glisse-le ici depuis l'Inspector.")]
    [SerializeField] private GameObject _vrStartGameMessage;

    [Tooltip("AudioSource de la musique de fond à mettre en pause pendant la vidéo d'intro. " +
             "Glisse le GameObject qui porte l'AudioSource depuis l'Inspector.")]
    [SerializeField] private AudioSource _backgroundMusic;

    // ═══════════════════════════════════════════════════════════════════════════
    //  INSPECTOR — VIDÉO
    // ═══════════════════════════════════════════════════════════════════════════

    [Header("Vidéo d'intro — obligatoire")]
    [Tooltip("Clip vidéo à jouer au lancement. Format H.264 recommandé, Transcode ✓ dans Unity.")]
    [SerializeField] private VideoClip _introClip;

    [Tooltip("Durée exacte de la vidéo en secondes. " +
             "Doit correspondre à la durée réelle du clip pour déclencher OnIntroVideoFinished " +
             "au bon moment. Par défaut : 40 s.")]
    [SerializeField] private float _videoDuration = 40f;

    [Header("Vidéo — Panels (créés automatiquement si vide)")]
    [Tooltip("Canvas World Space devant le casque VR. Créé automatiquement si null.")]
    [SerializeField] private GameObject _vrVideoPanel;

    [Tooltip("Canvas Screen Space Overlay pour l'AR. Créé automatiquement si null.")]
    [SerializeField] private GameObject _arVideoPanel;

    [Tooltip("RawImage dans le panel VR qui reçoit la RenderTexture. " +
             "Auto-détectée si panel assigné manuellement.")]
    [SerializeField] private RawImage _vrRawImage;

    [Tooltip("RawImage dans le panel AR qui reçoit la RenderTexture. " +
             "Auto-détectée si panel assigné manuellement.")]
    [SerializeField] private RawImage _arRawImage;

    [Tooltip("Résolution de la RenderTexture partagée entre les deux RawImage. " +
             "1920×1080 pour une qualité maximale, 1280×720 pour économiser la mémoire GPU.")]
    [SerializeField] private Vector2Int _renderTextureResolution = new Vector2Int(1920, 1080);

    [Tooltip("Scale du canvas VR (World Space). " +
             "0.002 → 3.84 m de large à 2 m (grand). " +
             "0.0015 → 2.88 m (moyen). 0.001 → 1.92 m (petit). " +
             "Ajuste à la volée sans recompiler.")]
    [SerializeField] private float _vrVideoPanelScale = 0.001f;

    // ═══════════════════════════════════════════════════════════════════════════
    //  INSPECTOR — CALIBRATION UI
    // ═══════════════════════════════════════════════════════════════════════════

    [Header("UI calibration — optionnel (créée automatiquement si vide)")]
    [Tooltip("Panel contenant le bouton Calibration (top-right). Créé auto si null.")]
    [SerializeField] private GameObject _buttonPanel;

    [Tooltip("Panel de feedback '✓ Calibré' affiché brièvement. Créé auto si null.")]
    [SerializeField] private GameObject _feedbackPanel;

    [Tooltip("Texte du feedback (auto-détecté depuis _feedbackPanel si null).")]
    [SerializeField] private TextMeshProUGUI _feedbackText;

    [Tooltip("Durée d'affichage du feedback (secondes).")]
    [SerializeField] private float _feedbackDuration = 3f;

    // ═══════════════════════════════════════════════════════════════════════════
    //  INSPECTOR — VUE AÉRIENNE
    // ═══════════════════════════════════════════════════════════════════════════

    [Header("Vue aérienne")]
    [Tooltip("Hauteur caméra AR au-dessus du centre de la map (unités virtuelles).")]
    [SerializeField] private float _aerialHeight = 75f;

    [Tooltip("Facteur d'échelle appliqué au XR Origin AR pour la vue aérienne. " +
             "0.01 → 500 unités virtuelles ≈ 5 m perçus (map entière visible à ses pieds).")]
    [SerializeField] private float _mapScale = 0.01f;

    [Tooltip("Rayon de la zone de jeu physique en mètres. " +
             "Détermine le remapping : 1 m physique = map_half_extent / physical_radius unités virtuelles.")]
    [SerializeField] private float _physicalPlayRadius = 2.5f;

    // ═══════════════════════════════════════════════════════════════════════════
    //  ÉTAT INTERNE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>True après une première calibration réussie.</summary>
    public static bool IsCalibrated { get; private set; }

    /// <summary>True quand la vidéo d'intro est terminée et que le gameplay a commencé.</summary>
    public static bool IsGameStarted { get; private set; }

    // ── Calibration UI ────────────────────────────────────────────────────────
    private Coroutine _feedbackRoutine;
    private GameObject _lancerJeuPanel; // créé à runtime dans CreerUIAutomatique()

    // ── Vue aérienne ──────────────────────────────────────────────────────────
    private bool    _aerialViewActive = false;
    private float   _remapFactor      = 100f;
    private Vector3 _camLocalAtLaunch = Vector3.zero;

    // ── Tracking physique continu ─────────────────────────────────────────────
    private bool        _trackingActive         = false;
    // Position physique du XR Origin sauvegardée juste avant la téléportation
    // en vue aérienne. Permet de reconstruire la position physique réelle même
    // quand le XR Origin est à (33, 75, 94) dans le monde virtuel.
    private Vector3     _physicalXROriginPos    = Vector3.zero;
    private PlayerSetup _remoteVRPlayer         = null;

    // ── Vidéo ─────────────────────────────────────────────────────────────────
    private VideoPlayer    _videoPlayer;
    private RenderTexture  _renderTexture;
    private bool           _videoIsPlaying  = false;
    // Référence au panel actif (VR ou AR) — partagée entre PlayIntroRoutine()
    // et le callback loopPointReached pour garantir qu'il est toujours caché.
    private GameObject     _activeVideoPanel = null;

    // ═══════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        // InitVideoPlayer() déplacé de Awake() vers Start() :
        // Sur iOS/Metal, VideoPlayer.SetTargetAudioSource(0, audioSrc) appelé en
        // Awake() peut crasher quand _introClip est null (0 audio tracks → index 0
        // invalide → exception native non catchée → crash au démarrage).
        // En Start(), le pipeline Metal est déjà initialisé → appel sûr.
        InitVideoPlayer();

        // Ce script (côté comportement AR) ne s'exécute pas sur le casque VR.
        // La partie réseau (NetworkBehaviour) reste active sur tous les clients
        // pour recevoir les RPC de synchronisation vidéo.
        if (XRRigSwitcher.IsVRMode)
        {
            // Sur VR : créer uniquement le panel vidéo VR (nécessaire pour afficher la vidéo).
            // La logique AR (calibration, UI, vue aérienne) reste désactivée.
            if (_vrVideoPanel == null)
                CreerVRVideoPanel();
            return;
        }

        // ── Auto-détection du XR Origin AR ───────────────────────────────────
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
            Debug.LogWarning("[ARProximityCalibration] _vrXROrigin non assigné.");

        // Masquer l'ancienne UI de calibration image si elle existe encore
        GameObject oldSearching = GameObject.Find("SearchingPanel");
        if (oldSearching != null) oldSearching.SetActive(false);

        // Créer l'UI bouton si rien n'est assigné en Inspector
        if (_buttonPanel == null)
            CreerUIAutomatique();

        // Créer le panel vidéo AR (côté AR uniquement)
        if (_arVideoPanel == null)
            CreerARVideoPanel();
    }

    private void Update()
    {
        Camera arCam = Camera.main;
        if (arCam == null) return;

        // Ne rien faire côté VR (pas de vue aérienne ni tracking AR)
        if (XRRigSwitcher.IsVRMode) return;

        // ── A. Tracking physique continu ──────────────────────────────────────
        if (_trackingActive)
            MettreAJourOffsetPhysique(arCam);

        // ── B. Remapping vue aérienne ─────────────────────────────────────────
        if (!_aerialViewActive || _arXROrigin == null) return;

        Vector3 camLocal = arCam.transform.localPosition - _camLocalAtLaunch;
        float   s        = _mapScale;
        Vector3 physWorld = _arXROrigin.rotation * camLocal;

        _arXROrigin.position = new Vector3(
            33f           + physWorld.x * (_remapFactor - s),
            _aerialHeight + physWorld.y * (_remapFactor - s),
            94f           + physWorld.z * (_remapFactor - s)
        );
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  VIDÉO — INITIALISATION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crée le VideoPlayer, la RenderTexture partagée et l'AudioSource.
    /// Appelé dans Awake() → disponible avant tout RPC réseau.
    /// </summary>
    private void InitVideoPlayer()
    {
        // RenderTexture partagée entre les deux RawImage (VR et AR)
        _renderTexture = new RenderTexture(
            _renderTextureResolution.x,
            _renderTextureResolution.y,
            0,
            RenderTextureFormat.ARGB32
        );
        _renderTexture.name = "IntroVideoRT";
        _renderTexture.Create();

        // VideoPlayer sur ce GO
        _videoPlayer = gameObject.AddComponent<VideoPlayer>();
        _videoPlayer.playOnAwake          = false;
        _videoPlayer.waitForFirstFrame    = true;
        _videoPlayer.isLooping            = false;
        _videoPlayer.renderMode           = VideoRenderMode.RenderTexture;
        _videoPlayer.targetTexture        = _renderTexture;
        _videoPlayer.audioOutputMode      = VideoAudioOutputMode.AudioSource;
        _videoPlayer.clip                 = _introClip;

        // Lie le son à l'AudioSource sur ce GO (ajouté via [RequireComponent])
        AudioSource audioSrc = GetComponent<AudioSource>();
        audioSrc.spatialBlend = 0f; // 2D — pas de spatialisation

        // Guard iOS : SetTargetAudioSource(0, …) ne doit être appelé que si
        // le VideoPlayer a au moins une piste audio (audioTrackCount > 0).
        // Avec un clip null, audioTrackCount = 0 → index 0 invalide → exception
        // native non catchée sur iOS/Metal → crash au démarrage.
        // Le binding est effectué à PrepareCompleted quand le clip est chargé.
        if (_introClip != null)
        {
            _videoPlayer.SetTargetAudioSource(0, audioSrc);
        }
        else
        {
            // Bind différé : dès que le clip est connu et préparé, on lie l'AudioSource.
            _videoPlayer.prepareCompleted += vp =>
            {
                if (vp.audioTrackCount > 0)
                    vp.SetTargetAudioSource(0, audioSrc);
            };
        }

        // Callback de fin natif (backup si _videoDuration est imprécis).
        // IMPORTANT : cache aussi _activeVideoPanel ici, car PlayIntroRoutine() vérifie
        // _videoIsPlaying après WaitForSeconds. Si loopPointReached fire en premier,
        // _videoIsPlaying passe à false → le if() dans la coroutine est sauté →
        // sans ce cache, le panel resterait visible indéfiniment (bug "écran noir AR").
        _videoPlayer.loopPointReached += _ =>
        {
            if (_videoIsPlaying)
            {
                _videoIsPlaying = false;

                // Cacher le panel (VR ou AR) quoi qu'il arrive
                if (_activeVideoPanel != null)
                {
                    _activeVideoPanel.SetActive(false);
                    _activeVideoPanel = null;
                }

                if (_backgroundMusic != null)
                    _backgroundMusic.UnPause();

                OnIntroVideoFinished();
            }
        };

        Debug.Log($"[ARProximityCalibration] VideoPlayer initialisé " +
                  $"(RT {_renderTextureResolution.x}×{_renderTextureResolution.y}).");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  VIDÉO — PANELS UI (création automatique)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crée le panel vidéo AR (Screen Space Overlay, sortingOrder 20).
    /// Couvre tout l'écran. Caché par défaut.
    /// </summary>
    private void CreerARVideoPanel()
    {
        GameObject canvasGO = new GameObject("AR_VideoCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20; // au-dessus du canvas de calibration (sortingOrder 10)

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Panel noir plein écran + RawImage
        GameObject panelGO = new GameObject("AR_VideoPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        Image bg   = panelGO.AddComponent<Image>();
        bg.color   = Color.black;

        RectTransform rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // RawImage pour afficher la vidéo
        GameObject rawGO = new GameObject("VideoRawImage");
        rawGO.transform.SetParent(panelGO.transform, false);

        _arRawImage         = rawGO.AddComponent<RawImage>();
        _arRawImage.texture = _renderTexture;

        RectTransform rawRT = rawGO.GetComponent<RectTransform>();
        rawRT.anchorMin = Vector2.zero;
        rawRT.anchorMax = Vector2.one;
        rawRT.offsetMin = Vector2.zero;
        rawRT.offsetMax = Vector2.zero;

        // On stocke le CANVAS (canvasGO) et non le panel interne (panelGO).
        // Raison : si on désactive seulement panelGO, canvasGO reste actif avec
        // sortingOrder=20 (ScreenSpaceOverlay) → fond noir visible après la vidéo.
        // Désactiver canvasGO garantit que RIEN du canvas n'est rendu après la vidéo.
        // Le panelGO (enfant) est actif par défaut → visible dès que canvasGO est activé.
        canvasGO.SetActive(false);   // caché initialement
        _arVideoPanel = canvasGO;    // contrôle via le canvas parent

        Debug.Log("[ARProximityCalibration] AR_VideoPanel créé automatiquement.");
    }

    /// <summary>
    /// Crée le panel vidéo VR (World Space Canvas face à la caméra VR).
    /// Positionné à 2 m devant la caméra VR, scale 0.002 (1080p → ~2m de large).
    /// Caché par défaut.
    /// </summary>
    private void CreerVRVideoPanel()
    {
        // On cherche la caméra VR pour positionner le canvas face à elle
        Camera vrCam = null;
        if (_vrCamera != null)
            vrCam = _vrCamera.GetComponent<Camera>();
        if (vrCam == null && XRRigSwitcher.IsVRMode)
            vrCam = Camera.main;

        GameObject canvasGO = new GameObject("VR_VideoCanvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Parenter à la caméra VR pour que le canvas suive le regard en permanence.
        // Sans parenting, le canvas reste fixe dans le monde et le joueur le perd
        // dès qu'il bouge la tête d'un millimètre.
        if (vrCam != null)
        {
            canvasGO.transform.SetParent(vrCam.transform, false);
            canvasGO.transform.localPosition = new Vector3(0f, 0f, 2f); // 2 m devant
            canvasGO.transform.localRotation = Quaternion.identity;
            canvasGO.transform.localScale    = Vector3.one * _vrVideoPanelScale;
            canvas.worldCamera               = vrCam; // nécessaire pour le rendu WorldSpace en VR
        }

        RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(1920f, 1080f);

        // Panel noir + RawImage
        GameObject panelGO = new GameObject("VR_VideoPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        Image bg = panelGO.AddComponent<Image>();
        bg.color = Color.black;

        RectTransform panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        GameObject rawGO = new GameObject("VideoRawImage");
        rawGO.transform.SetParent(panelGO.transform, false);

        _vrRawImage         = rawGO.AddComponent<RawImage>();
        _vrRawImage.texture = _renderTexture;

        RectTransform rawRT = rawGO.GetComponent<RectTransform>();
        rawRT.anchorMin = Vector2.zero;
        rawRT.anchorMax = Vector2.one;
        rawRT.offsetMin = Vector2.zero;
        rawRT.offsetMax = Vector2.zero;

        panelGO.SetActive(false);
        _vrVideoPanel = panelGO;

        Debug.Log("[ARProximityCalibration] VR_VideoPanel créé automatiquement.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  VIDÉO — SYNCHRONISATION RÉSEAU
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Point d'entrée appelé par OnLancerJeuButtonPressed().
    /// En Distributed Authority, on envoie directement un Rpc à Everyone.
    /// </summary>
    private void RequestLaunchIntroVideo()
    {
        if (_introClip == null)
        {
            Debug.LogWarning("[ARProximityCalibration] _introClip non assigné — " +
                             "la vidéo est ignorée, le jeu démarrera directement.");
            OnIntroVideoFinished();
            return;
        }

        PlayIntroVideoRpc();
    }

    /// <summary>
    /// Reçu par TOUS les clients (VR + AR), envoyable par n'importe quel client.
    /// InvokePermission.Everyone = compatible Distributed Authority (pas de serveur dédié).
    /// </summary>
    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    private void PlayIntroVideoRpc()
    {
        StartCoroutine(PlayIntroRoutine());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  VIDÉO — LECTURE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Joue la vidéo sur ce client :
    ///   1. Affiche le bon panel (VR World Space ou AR Screen Space).
    ///   2. Lance le VideoPlayer.
    ///   3. Attend _videoDuration secondes (ou le callback loopPointReached).
    ///   4. Cache le panel et appelle OnIntroVideoFinished().
    /// </summary>
    private IEnumerator PlayIntroRoutine()
    {
        if (_videoIsPlaying) yield break; // Évite les doubles appels
        _videoIsPlaying = true;

        // Assigner la RenderTexture aux RawImage (peut arriver après Awake sur VR)
        if (_vrRawImage != null && _vrRawImage.texture == null)
            _vrRawImage.texture = _renderTexture;
        if (_arRawImage != null && _arRawImage.texture == null)
            _arRawImage.texture = _renderTexture;

        // Afficher le bon panel selon la plateforme locale et le stocker dans
        // _activeVideoPanel pour que loopPointReached puisse aussi le cacher.
        _activeVideoPanel = XRRigSwitcher.IsVRMode ? _vrVideoPanel : _arVideoPanel;
        if (_activeVideoPanel != null)
            _activeVideoPanel.SetActive(true);
        else
            Debug.LogWarning("[ARProximityCalibration] Panel vidéo null pour ce client. " +
                             "La vidéo joue en arrière-plan sans rendu visible.");

        // Mettre la musique en pause pendant la vidéo
        if (_backgroundMusic != null && _backgroundMusic.isPlaying)
            _backgroundMusic.Pause();

        // Préparer et lancer
        _videoPlayer.time = 0;
        _videoPlayer.Play();

        Debug.Log($"[ARProximityCalibration] ▶ Vidéo d'intro lancée " +
                  $"({(XRRigSwitcher.IsVRMode ? "VR WorldSpace" : "AR ScreenSpace")}, " +
                  $"durée={_videoDuration}s).");

        // Attendre la durée déclarée.
        // Le callback loopPointReached sert de backup si le clip finit
        // légèrement avant _videoDuration (arrondi, frameRate variable).
        yield return new WaitForSeconds(_videoDuration);

        // Cacher le panel DANS TOUS LES CAS — même si loopPointReached a déjà
        // mis _videoIsPlaying à false (bug "écran noir AR" sans cette ligne).
        if (_activeVideoPanel != null)
        {
            _activeVideoPanel.SetActive(false);
            _activeVideoPanel = null;
        }

        // Stopper proprement si loopPointReached n'a pas déjà tout géré
        if (_videoIsPlaying)
        {
            _videoIsPlaying = false;
            _videoPlayer.Stop();

            if (_backgroundMusic != null)
                _backgroundMusic.UnPause();

            OnIntroVideoFinished();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  VIDÉO — FIN → LANCEMENT DU JEU
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Appelé quand la vidéo d'intro est terminée (sur TOUS les clients).
    /// C'est ici que le gameplay réel commence.
    ///
    /// Sur VR  : active le StartGameMessage + libère le CharacterController si besoin.
    /// Sur AR  : la vue aérienne est déjà active (lancée dans OnLancerJeuButtonPressed).
    ///           Rien de supplémentaire à faire côté AR, le remapping est déjà en cours.
    /// </summary>
    private void OnIntroVideoFinished()
    {
        Debug.Log("[ARProximityCalibration] ✅ Vidéo d'intro terminée — lancement du jeu.");

        // Signale à tous les systèmes (ex: MissileLauncher) que le gameplay peut commencer.
        IsGameStarted = true;

        

        // ── Message début de jeu (VR) ─────────────────────────────────────────
        if (_vrStartGameMessage != null)
        {
            _vrStartGameMessage.SetActive(true);
            StartCoroutine(CacherStartGameMessage(5f));
            Debug.Log("[ARProximityCalibration] ✓ StartGameMessage activé.");
        }

        // ── Tu peux ajouter ici tes propres événements de début de jeu ────────
        // Exemple :
        //   GameManager.Instance?.StartGame();
        //   AudioManager.Instance?.PlayGameMusic();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CALIBRATION — Bouton "Calibration"
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Appelée par le bouton "Calibration" sur l'interface AR.
    /// Le joueur AR doit tenir son téléphone CONTRE le casque VR.
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
            return;
        }

        // ── Trouver le joueur VR distant ──────────────────────────────────────
        if (_remoteVRPlayer == null)
            _remoteVRPlayer = TrouverJoueurVRDistant();

        if (_remoteVRPlayer == null)
        {
            AfficherFeedback("❌ Joueur VR non connecté !\nAttends que le VR rejoigne la session.");
            return;
        }

        Transform vrTarget = _remoteVRPlayer.transform;

        // ── Capturer le yaw VR avant alignement ───────────────────────────────
        float vrYaw = vrTarget.eulerAngles.y;
        Vector3 vrPosition = vrTarget.position;

        // ── Aligner le XR Origin AR sur la caméra VR ─────────────────────────
        AlignerARSurVR(arCam, vrTarget);

        // ── Activer le tracking physique continu ──────────────────────────────
        _trackingActive = true;

        float   arYaw      = arCam.transform.eulerAngles.y;
        Vector3 arPosition = arCam.transform.position;

        // ── Sauvegarder la calibration ────────────────────────────────────────
        CalibrationPersistence.SaveCalibration(vrYaw, vrPosition, arYaw, arPosition);

        // ── Sync réseau → tous les clients ────────────────────────────────────
        if (_remoteVRPlayer != null)
            _remoteVRPlayer.SyncCalibrationToAll(vrYaw, vrPosition, arYaw, arPosition);
        else
            Debug.LogWarning("[ARProximityCalibration] _remoteVRPlayer null — " +
                             "calibration NON synchronisée vers VR.");

        IsCalibrated = true;
        AfficherFeedback("✓ Calibration réussie !");

        if (_lancerJeuPanel != null)
            _lancerJeuPanel.SetActive(true);

        Debug.Log($"[ARProximityCalibration] ✓ Calibration OK — " +
                  $"vrYaw={vrYaw:F1}°  vrPos={vrPosition:F3}  " +
                  $"arYaw={arYaw:F1}°  arPos={arPosition:F3}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CALIBRATION — Bouton "Lancer le jeu"
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Téléporte le joueur AR en vue aérienne, fige la position finale,
    /// puis demande à TOUS les clients de jouer la vidéo d'intro.
    /// Le jeu démarre réellement à la fin de la vidéo (OnIntroVideoFinished).
    ///
    /// ORDRE STRICT (important) :
    ///   0. Figer et synchroniser la position AR finale.
    ///   1. Désactiver CharacterController.
    ///   2. Appliquer le scale sur _arXROrigin EN PREMIER.
    ///   3. Positionner pour centrer la caméra au-dessus du centre de la map.
    ///   4. Activer le remapping continu physique → virtuel.
    ///   5. Cacher _lancerJeuPanel.
    ///   6. Réactiver CharacterController.
    ///   7. Synchroniser VR Origin.
    ///   8. Lancer la vidéo d'intro sur tous les clients (NEW).
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

        // ── 0. Figer et synchroniser la position AR finale ────────────────────
        {
            MettreAJourOffsetPhysique(arCam);

            // Sauvegarder la position PHYSIQUE du XR Origin avant sa téléportation
            // en vue aérienne (steps 2-3). Après ça, arCam.transform.position sera
            // virtuel (~75 unités) mais arCam.localPosition reste physique.
            // MettreAJourOffsetPhysique utilisera _physicalXROriginPos pour
            // reconstruire la vraie position monde physique pendant la vue aérienne.
            _physicalXROriginPos = _arXROrigin.position;
            // Le tracking continue — le joueur AR se déplace physiquement pendant
            // la vue aérienne et sa position physique réelle doit rester à jour.

            float   finalArYaw = CalibrationPersistence.ArYaw;
            Vector3 finalArPos = CalibrationPersistence.ArPhysicalPosition;

            if (_remoteVRPlayer != null)
                _remoteVRPlayer.SyncFinalARSpawnToAll(finalArYaw, finalArPos);
            else
                Debug.LogWarning("[ARProximityCalibration] _remoteVRPlayer null — " +
                                 "FinalARSpawn NON synchronisé vers VR.");

            Debug.Log($"[ARProximityCalibration] FinalARSpawn figé — arYaw={finalArYaw:F1}°  arPos={finalArPos:F3}");
        }

        // ── 1. Désactiver CharacterController ────────────────────────────────
        CharacterController cc = _arXROrigin.GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;

        // ── 2. Appliquer le scale ─────────────────────────────────────────────
        _arXROrigin.localScale = Vector3.one * _mapScale;

        // ── 3. Centrer la caméra au-dessus du centre de la map ───────────────
        _arXROrigin.position = new Vector3(33f, _aerialHeight, 94f);

        // ── 4. Activer le remapping continu ───────────────────────────────────
        _remapFactor      = 250f / Mathf.Max(0.1f, _physicalPlayRadius);
        _camLocalAtLaunch = arCam.transform.localPosition;
        _aerialViewActive = true;

        // ── 5. Cacher le bouton ───────────────────────────────────────────────
        if (_lancerJeuPanel != null)
            _lancerJeuPanel.SetActive(false);

        // ── 6. Réactiver CharacterController ─────────────────────────────────
        if (cc != null) cc.enabled = true;

        // ── 7. Aligner l'Origin VR sur le centre de la map ───────────────────
        if (_vrXROrigin != null)
        {
            _vrXROrigin.position = Vector3.zero;
            _vrXROrigin.rotation = Quaternion.identity;
        }

        Debug.Log($"[ARProximityCalibration] 🌍 Vue aérienne — " +
                  $"scale={_arXROrigin.localScale.x:F4}  remapFactor={_remapFactor:F1}  " +
                  $"physRadius={_physicalPlayRadius:F1}m");

        // ── 8. Lancer la vidéo d'intro sur TOUS les clients ───────────────────
        // La vidéo démarre immédiatement sur AR et VR.
        // OnIntroVideoFinished() est appelé localement sur chaque client à la fin.
        RequestLaunchIntroVideo();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  MATHÉMATIQUE D'ALIGNEMENT
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Repositionne et réoriente le RIG AR pour placer le joueur AR
    /// juste devant le joueur VR, face à lui (face-à-face).
    ///
    /// ORDRE : rotation AVANT translation.
    /// OFFSET : 10 cm dans la direction de regard du joueur VR.
    /// </summary>
    private void AlignerARSurVR(Camera arCam, Transform vrTarget)
    {
        // ── Étape 1 : aligner le yaw pour placement FACE À FACE ─────────────
        float arYaw   = arCam.transform.eulerAngles.y;
        float vrYaw   = vrTarget.eulerAngles.y;
        float deltYaw = Mathf.DeltaAngle(arYaw, vrYaw + 180f);
        _arXROrigin.Rotate(0f, deltYaw, 0f, Space.World);

        // ── Étape 2 : positionner le RIG en compensant localPos ARCore ───────
        Vector3 vrPos       = vrTarget.position;
        Vector3 vrForwardXZ = Quaternion.Euler(0f, vrYaw, 0f) * Vector3.forward;
        const float offsetDevant = 0.1f;

        Vector3 desiredCamXZ = new Vector3(
            vrPos.x + vrForwardXZ.x * offsetDevant,
            vrPos.y,
            vrPos.z + vrForwardXZ.z * offsetDevant
        );

        Vector3 camLocalFlat    = new Vector3(arCam.transform.localPosition.x, 0f, arCam.transform.localPosition.z);
        Vector3 camWorldOffsetXZ = _arXROrigin.rotation * camLocalFlat;

        _arXROrigin.position = new Vector3(
            desiredCamXZ.x - camWorldOffsetXZ.x,
            desiredCamXZ.y,
            desiredCamXZ.z - camWorldOffsetXZ.z
        );

        Debug.Log($"[ARProximityCalibration] AlignerARSurVR — " +
                  $"vrPos={vrPos:F3}  vrYaw={vrYaw:F1}°  " +
                  $"camLocalFlat={camLocalFlat:F3}  camWorldOffset={camWorldOffsetXZ:F3}  " +
                  $"rig AR → {_arXROrigin.position:F3}  cam≈{desiredCamXZ:F3} (10 cm devant VR)");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  TRACKING PHYSIQUE
    // ═══════════════════════════════════════════════════════════════════════════

    private void MettreAJourOffsetPhysique(Camera arCam)
    {
        if (CalibrationPersistence.Instance == null) return;

        Vector3 physicalPos;

        if (_aerialViewActive)
        {
            // En vue aérienne, arCam.transform.position est virtuel (XR Origin à 75 unités).
            // On reconstruit la position physique monde :
            //   pos_physique = _physicalXROriginPos + XROriginRot * cam.localPosition
            // XROriginRot ne change pas depuis la calibration (seule la position change).
            Vector3 localPos = arCam.transform.localPosition;
            localPos.y = 0f;
            physicalPos = _physicalXROriginPos + _arXROrigin.rotation * localPos;
        }
        else
        {
            physicalPos = arCam.transform.position;
            physicalPos.y = 0f;
        }

        CalibrationPersistence.UpdateAR(arCam.transform.eulerAngles.y, physicalPos);
    }

    private PlayerSetup TrouverJoueurVRDistant()
    {
        foreach (var ps in FindObjectsByType<PlayerSetup>(FindObjectsSortMode.None))
        {
            if (!ps.IsOwner)
                return ps;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  FEEDBACK UI
    // ═══════════════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════════════
    //  CRÉATION AUTOMATIQUE — UI CALIBRATION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crée une UI Screen Space Overlay minimale si _buttonPanel n'est pas assigné en Inspector.
    /// Panels créés :
    ///   • CalibrationButtonPanel  — top-right, toujours visible.
    ///   • CalibrationFeedbackPanel — top-center, caché par défaut.
    ///   • LancerJeuPanel          — centre écran, caché par défaut.
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

        Image btnImg  = btnGO.AddComponent<Image>();
        btnImg.color  = new Color(0.08f, 0.55f, 1f, 0.92f);

        RectTransform btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin     = Vector2.zero;
        btnRT.anchorMax     = Vector2.one;
        btnRT.offsetMin     = new Vector2(8f, 8f);
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
        lbl.text      = "⊕  Calibration";
        lbl.fontSize  = 28f;
        lbl.fontStyle = FontStyles.Bold;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.color     = Color.white;

        RectTransform lblRT = lbl.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;

        _buttonPanel = panelGO;

        // ── Panel feedback (top-center) ───────────────────────────────────────
        GameObject fbGO = new GameObject("CalibrationFeedbackPanel");
        fbGO.transform.SetParent(canvasGO.transform, false);
        fbGO.SetActive(false);

        Image fbImg         = fbGO.AddComponent<Image>();
        fbImg.color         = new Color(0.06f, 0.55f, 0.22f, 0.88f);
        fbImg.raycastTarget = false;

        RectTransform fbRT    = fbGO.GetComponent<RectTransform>();
        fbRT.anchorMin        = new Vector2(0.5f, 1f);
        fbRT.anchorMax        = new Vector2(0.5f, 1f);
        fbRT.pivot            = new Vector2(0.5f, 1f);
        fbRT.anchoredPosition = new Vector2(0f, -80f);
        fbRT.sizeDelta        = new Vector2(420f, 70f);

        GameObject ftGO = new GameObject("FeedbackText");
        ftGO.transform.SetParent(fbGO.transform, false);

        TextMeshProUGUI ft = ftGO.AddComponent<TextMeshProUGUI>();
        ft.text        = "✓ Calibration réussie !";
        ft.fontSize    = 26f;
        ft.alignment   = TextAlignmentOptions.Center;
        ft.color       = Color.white;
        ft.raycastTarget = false;

        RectTransform ftRT = ft.GetComponent<RectTransform>();
        ftRT.anchorMin = Vector2.zero;
        ftRT.anchorMax = Vector2.one;
        ftRT.offsetMin = new Vector2(12f, 4f);
        ftRT.offsetMax = new Vector2(-12f, -4f);

        _feedbackPanel = fbGO;
        _feedbackText  = ft;

        // ── Panel "Lancer le jeu" (centre écran, caché par défaut) ───────────
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

        Image ljBtnImg  = ljBtnGO.AddComponent<Image>();
        ljBtnImg.color  = new Color(0.95f, 0.60f, 0.05f, 0.95f);

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
        ljLbl.text      = "🌍  Lancer le jeu";
        ljLbl.fontSize  = 34f;
        ljLbl.fontStyle = FontStyles.Bold;
        ljLbl.alignment = TextAlignmentOptions.Center;
        ljLbl.color     = Color.white;

        RectTransform ljLblRT = ljLbl.GetComponent<RectTransform>();
        ljLblRT.anchorMin = Vector2.zero;
        ljLblRT.anchorMax = Vector2.one;
        ljLblRT.offsetMin = Vector2.zero;
        ljLblRT.offsetMax = Vector2.zero;

        _lancerJeuPanel = ljGO;

        Debug.Log("[ARProximityCalibration] UI créée automatiquement " +
                  "(CalibrationButton + FeedbackPanel + LancerJeuPanel).");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CLEANUP
    // ═══════════════════════════════════════════════════════════════════════════

    public override void OnDestroy()
    {
        base.OnDestroy();
        // Libérer la RenderTexture GPU proprement
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }
}