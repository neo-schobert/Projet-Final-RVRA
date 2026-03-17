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
///     3. Aligne le XR Origin AR sur l'avatar VR dans l'espace virtuel partagé.
///
/// MATHÉMATIQUE DE L'ALIGNEMENT (étape 3) :
///   1. Rotation   : yaw AR XROrigin += (yaw_VR_avatar − yaw_AR_camera)
///                   (rotation AVANT translation — même principe que VRCalibration)
///   2. Translation: AR XROrigin += (VR_avatar.pos − AR_camera.pos) en XZ
///                   (lu APRÈS rotation car la rotation déplace la caméra)
///
/// FONCTIONNEMENT RÉSEAU :
///   FindAnyObjectByType<PlayerSetup>() retourne l'avatar VR.
///   PlayerSetup est UNIQUEMENT sur le prefab VR → sur le client AR,
///   l'unique instance trouvée est forcément l'avatar VR distant (réplique réseau).
///   Sa position = Camera.main du joueur VR (synchronisée par ClientNetworkTransform).
///
/// SETUP dans la scène :
///   A. Ce script est sur le GameObject "Calibration Manager".
///   B. _arXROrigin → XR Origin (Mobile AR) Transform.
///   C. Laisser _buttonPanel / _feedbackPanel vides : l'UI est créée automatiquement.
///      OU assigner des GameObjects personnalisés depuis l'Inspector.
/// </summary>
public class ARProximityCalibration : MonoBehaviour
{
    [Header("XR — obligatoire")]
    [Tooltip("Transform du XR Origin (Mobile AR). Auto-détecté si non assigné.")]
    [SerializeField] private Transform _arXROrigin;

    [Header("UI — optionnel (créée automatiquement si vide)")]
    [Tooltip("Panel contenant le bouton Calibration (top-right). Créé auto si null.")]
    [SerializeField] private GameObject _buttonPanel;

    [Tooltip("Panel de feedback '✓ Calibré' affiché brièvement. Créé auto si null.")]
    [SerializeField] private GameObject _feedbackPanel;

    [Tooltip("Texte du feedback (auto-détecté depuis _feedbackPanel si null).")]
    [SerializeField] private TextMeshProUGUI _feedbackText;

    [Tooltip("Durée d'affichage du feedback (secondes).")]
    [SerializeField] private float _feedbackDuration = 3f;

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>True après une première calibration réussie.</summary>
    public static bool IsCalibrated { get; private set; }

    private Coroutine _feedbackRoutine;

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

        // Masquer l'ancienne UI de calibration image si elle existe encore
        GameObject oldSearching = GameObject.Find("SearchingPanel");
        if (oldSearching != null) oldSearching.SetActive(false);

        // Créer l'UI bouton si rien n'est assigné en Inspector
        if (_buttonPanel == null)
            CreerUIAutomatique();
    }

    // ── Calibration (appelée par le bouton onClick) ────────────────────────────

    /// <summary>
    /// Appelée par le bouton "Calibration" sur l'interface AR.
    /// Le joueur AR doit tenir son téléphone CONTRE le casque VR au moment de l'appui.
    /// Capture l'offset physique AR→VR, le sauvegarde, puis aligne le XR Origin AR.
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

        // Chercher l'avatar VR sur le réseau.
        // PlayerSetup est UNIQUEMENT sur le prefab VR.
        // Sur le client AR, l'instance trouvée est donc l'avatar distant du joueur VR.
        PlayerSetup vrAvatar = FindAnyObjectByType<PlayerSetup>();

        if (vrAvatar == null)
        {
            AfficherFeedback("❌ Joueur VR non trouvé.\nConnecte le joueur VR d'abord.");
            Debug.LogWarning("[ARProximityCalibration] Aucun PlayerSetup dans la scène. " +
                             "Le joueur VR est-il connecté ?");
            return;
        }

        // ── Étape 1 : capturer les positions physiques AVANT alignement ──────
        // L'offset doit refléter la distance réelle entre les deux joueurs,
        // avant que le XR Origin AR soit déplacé.
        Vector3 arPhysPos = arCam.transform.position;
        Vector3 vrPhysPos = vrAvatar.transform.position;

        // ── Étape 2 : sauvegarder l'offset dans CalibrationPersistence ───────
        // Utilisé par TempleSceneManager pour le spawn AR dans TempleScene.
        CalibrationPersistence.SaveOffset(arPhysPos, vrPhysPos);

        // ── Étape 3 : aligner le XR Origin AR sur l'avatar VR (une seule fois) ─
        // AR Foundation maintient ensuite la cohérence du tracking dans ce référentiel.
        AlignerARSurVR(arCam, vrAvatar.transform);

        IsCalibrated = true;
        AfficherFeedback("✓ Calibration réussie !");

        Debug.Log($"[ARProximityCalibration] ✓ Calibration OK — " +
                  $"VR pos={vrPhysPos:F3}  AR cam={arPhysPos:F3}  " +
                  $"offset={CalibrationPersistence.PhysicalOffset:F3}  " +
                  $"spawnDist={CalibrationPersistence.SpawnDistance:F3}m");
    }

    // ── Mathématique d'alignement ─────────────────────────────────────────────

    /// <summary>
    /// Repositionne _arXROrigin de sorte que la caméra AR atterrisse sur
    /// la position de l'avatar VR dans l'espace virtuel partagé.
    ///
    /// ORDRE IMPÉRATIF : rotation AVANT translation.
    /// Raison : la rotation pivote autour du XR Origin (pas de la caméra).
    /// Après rotation, la position de la caméra a changé — on doit la relire
    /// pour calculer l'offset de translation correct.
    /// </summary>
    private void AlignerARSurVR(Camera arCam, Transform vrAvatar)
    {
        // ── Étape 1 : aligner le yaw ─────────────────────────────────────────
        float arYaw   = arCam.transform.eulerAngles.y;
        float vrYaw   = vrAvatar.eulerAngles.y;
        float deltYaw = Mathf.DeltaAngle(arYaw, vrYaw);   // [-180 ; +180]

        _arXROrigin.Rotate(0f, deltYaw, 0f, Space.World);

        // ── Étape 2 : aligner la position XZ (après rotation) ────────────────
        // La caméra a bougé suite à la rotation du pivot XROrigin.
        // On relit sa position APRÈS la rotation.
        Vector3 arCamAfterRot = arCam.transform.position;
        Vector3 vrPos         = vrAvatar.position;

        _arXROrigin.position += new Vector3(
            vrPos.x - arCamAfterRot.x,
            0f,
            vrPos.z - arCamAfterRot.z
        );
    }

    // ── Feedback UI ───────────────────────────────────────────────────────────

    /// <summary>Affiche un message de feedback temporaire à l'écran.</summary>
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

    // ── Création automatique de l'UI ─────────────────────────────────────────

    /// <summary>
    /// Crée une UI Screen Space Overlay minimale si aucun panel n'est assigné :
    ///   • Onglet "Calibration" en haut à droite (toujours visible).
    ///   • Banner de feedback centré en haut (caché par défaut).
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

        // ── Panel bouton (top-right) ──────────────────────────────────────────
        GameObject panelGO = new GameObject("CalibrationButtonPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        Image panelImg         = panelGO.AddComponent<Image>();
        panelImg.color         = new Color(0f, 0f, 0f, 0.55f);
        panelImg.raycastTarget = true;

        RectTransform panelRT    = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(1f, 1f);
        panelRT.anchorMax        = new Vector2(1f, 1f);
        panelRT.pivot            = new Vector2(1f, 1f);
        panelRT.anchoredPosition = new Vector2(-24f, -24f);
        panelRT.sizeDelta        = new Vector2(220f, 70f);

        // Bouton
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

        // Label du bouton
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

        // ── Panel feedback (top-center, caché par défaut) ─────────────────────
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
        fbRT.anchoredPosition = new Vector2(0f, -24f);
        fbRT.sizeDelta        = new Vector2(420f, 70f);

        // Texte de feedback
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

        Debug.Log("[ARProximityCalibration] UI créée automatiquement (aucun panel assigné dans l'Inspector).");
    }
}
