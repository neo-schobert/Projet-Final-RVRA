using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
#if UNITY_EDITOR
using Unity.Netcode.Editor;
using UnityEditor;

[CustomEditor(typeof(PlayerController), true)]
public class PlayerControllerEditor : NetworkTransformEditor
{
    private SerializedProperty m_Speed;
    private SerializedProperty m_SprintMultiplier;
    private SerializedProperty m_SprintSmooth;
    private SerializedProperty m_MouseSensitivity;
    private SerializedProperty m_FirstPersonCam;
    private SerializedProperty m_ThirdPersonCam;
    private SerializedProperty m_Animator;
    private SerializedProperty m_NetworkAnimator;
    private SerializedProperty m_BodyRenderer;
    private SerializedProperty m_PlayerMaterials;
    private SerializedProperty m_CursorObject;

    public override void OnEnable()
    {
        m_Speed            = serializedObject.FindProperty(nameof(PlayerController.Speed));
        m_SprintMultiplier = serializedObject.FindProperty(nameof(PlayerController.SprintMultiplier));
        m_SprintSmooth     = serializedObject.FindProperty(nameof(PlayerController.SprintSmooth));
        m_MouseSensitivity = serializedObject.FindProperty(nameof(PlayerController.MouseSensitivity));
        m_FirstPersonCam   = serializedObject.FindProperty("firstPersonCam");
        m_ThirdPersonCam   = serializedObject.FindProperty("thirdPersonCam");
        m_Animator         = serializedObject.FindProperty("animator");
        m_NetworkAnimator  = serializedObject.FindProperty("networkAnimator");
        m_BodyRenderer     = serializedObject.FindProperty("bodyRenderer");
        m_PlayerMaterials  = serializedObject.FindProperty("playerMaterials");
        m_CursorObject     = serializedObject.FindProperty("cursorObject");
        base.OnEnable();
    }

    private void DisplayPlayerControllerProperties()
    {
        EditorGUILayout.LabelField("Movement Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_Speed);
        EditorGUILayout.PropertyField(m_SprintMultiplier);
        EditorGUILayout.PropertyField(m_SprintSmooth);
        EditorGUILayout.PropertyField(m_MouseSensitivity);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_FirstPersonCam);
        EditorGUILayout.PropertyField(m_ThirdPersonCam);
        EditorGUILayout.PropertyField(m_Animator);
        EditorGUILayout.PropertyField(m_NetworkAnimator);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_BodyRenderer);
        EditorGUILayout.PropertyField(m_PlayerMaterials);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("UI", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_CursorObject);
    }

    public override void OnInspectorGUI()
    {
        var p = target as PlayerController;
        void SetExpanded(bool ex) { p.PlayerControllerPropertiesVisible = ex; }
        DrawFoldOutGroup<PlayerController>(p.GetType(), DisplayPlayerControllerProperties, p.PlayerControllerPropertiesVisible, SetExpanded);
        base.OnInspectorGUI();
    }
}
#endif

public class PlayerController : NetworkTransform
{
#if UNITY_EDITOR
    public bool PlayerControllerPropertiesVisible;
#endif

    [HideInInspector] public float Speed            = 3f;
    [HideInInspector] public float SprintMultiplier = 2f;
    [HideInInspector] public float SprintSmooth     = 5f;
    [HideInInspector] public float MouseSensitivity = 2f;

    [SerializeField] private Camera              firstPersonCam;
    [SerializeField] private Camera              thirdPersonCam;
    [SerializeField] private Animator            animator;
    [SerializeField] private NetworkAnimator     networkAnimator;
    [SerializeField] private SkinnedMeshRenderer bodyRenderer;
    [SerializeField] private Material[]          playerMaterials;
    [SerializeField] private GameObject          cursorObject;    // Curseur 3D (cube) visible uniquement par le owner

    // ── En Distributed Authority, le Owner a l'autorité sur son propre objet ──
    private NetworkVariable<int> playerMaterialIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    // ── Head bob parameters ───────────────────────────────────────────────────
    private const float BobFreqWalk    = 1.8f;
    private const float BobFreqSprint  = 2.6f;
    private const float BobAmplitudeY  = 0.06f;
    private const float BobAmplitudeX  = 0.02f;
    private const float BobReturnSpeed = 8f;
    private const float BobBlendSpeed  = 6f;

    // ── Private state ─────────────────────────────────────────────────────────
    private bool    isFirstPerson   = true;
    private bool    isPaused        = false;
    private bool    isCursorDriving = false;  // true quand Alt est enfoncé (CursorDriver actif)
    private float   yaw             = 0f;
    private float   pitch           = 0f;
    private float   currentSprint   = 1f;

    // Head-bob state
    private float   bobPhase        = 0f;
    private float   bobIntensity    = 0f;
    private Vector3 bobOffset       = Vector3.zero;
    private Vector3 fpCamBaseLocalPos;

    // ─────────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (firstPersonCam  == null) Debug.LogError("[PlayerController] firstPersonCam non assignée !");
        if (thirdPersonCam  == null) Debug.LogError("[PlayerController] thirdPersonCam non assignée !");
        if (animator        == null) Debug.LogError("[PlayerController] Animator non assigné !");
        if (networkAnimator == null) Debug.LogError("[PlayerController] NetworkAnimator non assigné !");
        if (bodyRenderer    == null) Debug.LogError("[PlayerController] bodyRenderer non assigné !");
        if (playerMaterials == null || playerMaterials.Length == 0)
            Debug.LogError("[PlayerController] playerMaterials vide !");
        if (cursorObject    == null) Debug.LogWarning("[PlayerController] cursorObject non assigné !");

        if (firstPersonCam != null)
            fpCamBaseLocalPos = firstPersonCam.transform.localPosition;

        // ── Assignation du material ───────────────────────────────────────────
        if (IsOwner)
            playerMaterialIndex.Value = (int)(OwnerClientId % (ulong)playerMaterials.Length);

        playerMaterialIndex.OnValueChanged += OnMaterialIndexChanged;
        ApplyMaterial(playerMaterialIndex.Value);
        // ─────────────────────────────────────────────────────────────────────

        SetCameraState(false, false);

        // Curseur 3D caché par défaut pour tout le monde
        if (cursorObject != null)
            cursorObject.SetActive(false);

        if (IsOwner)
        {
            SetCameraState(true, false);
            UpdateMeshVisibility();

            // Le cube curseur n'est visible que pour le owner
            if (cursorObject != null)
                cursorObject.SetActive(true);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        playerMaterialIndex.OnValueChanged -= OnMaterialIndexChanged;
    }

    // ── CursorDriver interface ────────────────────────────────────────────────

    // Appelé par CursorDriver.cs quand Alt est enfoncé (true) ou relâché (false).
    // On gère uniquement le lock souris — le cursorObject (cube) n'est jamais touché ici.
    public void SetCursorDriverActive(bool driverActive)
    {
        if (!IsOwner) return;
    
        isCursorDriving = driverActive;
    
        if (driverActive)
        {
            Cursor.lockState = CursorLockMode.Confined; // ou None
            Cursor.visible   = true;  // ← était false, c'est le bug
        }
        else
        {
            if (!isPaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
            }
        }
    }

    // ── Appearance ────────────────────────────────────────────────────────────

    private void OnMaterialIndexChanged(int oldIndex, int newIndex) => ApplyMaterial(newIndex);

    private void ApplyMaterial(int index)
    {
        if (bodyRenderer == null || playerMaterials == null || playerMaterials.Length == 0) return;
        if (index < 0 || index >= playerMaterials.Length) return;
        bodyRenderer.sharedMaterial = playerMaterials[index];
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsSpawned) return;
        if (!IsOwner)   return;

        HandleCameraSwitch();
        if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();
        if (isPaused) return;

        // Rotation et mouvement suspendus pendant que le CursorDriver contrôle la souris
        if (isCursorDriving) return;

        HandleRotation();
        HandleMovement();
        HandleHeadBob();
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;

        if (isFirstPerson && firstPersonCam != null)
        {
            firstPersonCam.transform.localPosition = fpCamBaseLocalPos + bobOffset;
            firstPersonCam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        if (!isFirstPerson && thirdPersonCam != null)
        {
            thirdPersonCam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    // ── Camera switch ─────────────────────────────────────────────────────────

    private void HandleCameraSwitch()
    {
        if (!Input.GetKeyDown(KeyCode.F5)) return;

        isFirstPerson = !isFirstPerson;
        SetCameraState(isFirstPerson, !isFirstPerson);
        UpdateMeshVisibility();

        if (!isPaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    private void HandleRotation()
    {
        yaw   += Input.GetAxis("Mouse X") * MouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * MouseSensitivity;
        pitch  = Mathf.Clamp(pitch, -80f, 80f);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        bool  isSprinting  = Input.GetKey(KeyCode.LeftShift);
        float targetSprint = isSprinting ? SprintMultiplier : 1f;
        currentSprint = Mathf.Lerp(currentSprint, targetSprint, SprintSmooth * Time.deltaTime);

        float   moveX     = Input.GetAxis("Horizontal");
        float   moveZ     = Input.GetAxis("Vertical");
        Vector3 direction = transform.right * moveX + transform.forward * moveZ;
        Vector3 move      = direction.normalized
                            * Mathf.Clamp01(direction.magnitude)
                            * Speed * currentSprint * Time.deltaTime;
        transform.position += move;

        if (animator != null)
        {
            float blendX = Mathf.Clamp(moveX * currentSprint, -SprintMultiplier, SprintMultiplier);
            float blendZ = Mathf.Clamp(moveZ * currentSprint, -SprintMultiplier, SprintMultiplier);
            animator.SetFloat("SpeedX", blendX, 0.1f, Time.deltaTime);
            animator.SetFloat("SpeedZ", blendZ, 0.1f, Time.deltaTime);
        }
    }

    // ── Head bob ──────────────────────────────────────────────────────────────

    private void HandleHeadBob()
    {
        float moveX         = Input.GetAxis("Horizontal");
        float moveZ         = Input.GetAxis("Vertical");
        float moveMagnitude = Mathf.Clamp01(new Vector2(moveX, moveZ).magnitude);

        float targetIntensity = moveMagnitude;
        bobIntensity = Mathf.Lerp(bobIntensity, targetIntensity,
                                  (targetIntensity > bobIntensity ? BobBlendSpeed : BobReturnSpeed)
                                  * Time.deltaTime);

        if (bobIntensity > 0.01f)
        {
            float sprintT = (currentSprint - 1f) / Mathf.Max(SprintMultiplier - 1f, 0.001f);
            float freq    = Mathf.Lerp(BobFreqWalk, BobFreqSprint, sprintT);
            bobPhase     += 2f * Mathf.PI * freq * Time.deltaTime;
            bobPhase      = bobPhase % (2f * Mathf.PI);
        }

        if (bobIntensity < 0.01f)
        {
            bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, BobReturnSpeed * Time.deltaTime);
            return;
        }

        float targetY = Mathf.Sin(bobPhase)        * BobAmplitudeY * bobIntensity;
        float targetX = Mathf.Sin(bobPhase * 0.5f) * BobAmplitudeX * bobIntensity;

        bobOffset = new Vector3(targetX, targetY, 0f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetCameraState(bool firstPerson, bool thirdPerson)
    {
        if (firstPersonCam != null) firstPersonCam.enabled = firstPerson;
        if (thirdPersonCam != null) thirdPersonCam.enabled = thirdPerson;
    }

    private void UpdateMeshVisibility()
    {
        if (bodyRenderer != null)
            bodyRenderer.enabled = !isFirstPerson;
    }

    private void TogglePause()
    {
        isPaused         = !isPaused;
        Cursor.lockState = isPaused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = isPaused;
        // cursorObject non touché : le cube reste toujours actif pour le owner
    }

    private void OnGUI()
    {
        if (!isPaused || !IsOwner) return;

        float w = 200f, h = 100f;
        GUILayout.BeginArea(new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h), GUI.skin.box);
        GUILayout.Label("PAUSE");
        if (GUILayout.Button("Resume")) TogglePause();
        if (GUILayout.Button("Quit"))   Application.Quit();
        GUILayout.EndArea();
    }
}