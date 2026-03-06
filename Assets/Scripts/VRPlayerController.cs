using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
#if UNITY_EDITOR
using Unity.Netcode.Editor;
using UnityEditor;

[CustomEditor(typeof(VRPlayerController), true)]
public class VRPlayerControllerEditor : NetworkTransformEditor
{
    private SerializedProperty m_BodyRenderer;
    private SerializedProperty m_PlayerMaterials;
    private SerializedProperty m_Animator;
    private SerializedProperty m_NetworkAnimator;
    private SerializedProperty m_XRRig;

    public override void OnEnable()
    {
        m_BodyRenderer    = serializedObject.FindProperty("bodyRenderer");
        m_PlayerMaterials = serializedObject.FindProperty("playerMaterials");
        m_Animator        = serializedObject.FindProperty("animator");
        m_NetworkAnimator = serializedObject.FindProperty("networkAnimator");
        m_XRRig           = serializedObject.FindProperty("xrRig");
        base.OnEnable();
    }

    private void DisplayVRPlayerControllerProperties()
    {
        EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_BodyRenderer);
        EditorGUILayout.PropertyField(m_PlayerMaterials);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_Animator);
        EditorGUILayout.PropertyField(m_NetworkAnimator);
        EditorGUILayout.PropertyField(m_XRRig);
    }

    public override void OnInspectorGUI()
    {
        var p = target as VRPlayerController;
        if (p == null) return;

        void SetExpanded(bool ex) { p.VRPlayerControllerPropertiesVisible = ex; }
        DrawFoldOutGroup<VRPlayerController>(p.GetType(), DisplayVRPlayerControllerProperties, p.VRPlayerControllerPropertiesVisible, SetExpanded);
        base.OnInspectorGUI();
    }
}
#endif

/// <summary>
/// Version VR du PlayerController.
/// Le déplacement est géré par le XR Rig.
/// La vélocité world-space est convertie dans le repère horizontal de la tête (yaw)
/// pour alimenter SpeedX / SpeedZ — même convention que PlayerController desktop.
/// </summary>
public class VRPlayerController : NetworkTransform
{
#if UNITY_EDITOR
    public bool VRPlayerControllerPropertiesVisible;
#endif

    [SerializeField] private SkinnedMeshRenderer bodyRenderer;
    [SerializeField] private Material[]          playerMaterials;
    [SerializeField] private Animator            animator;
    [SerializeField] private NetworkAnimator     networkAnimator;
    [SerializeField] private Transform           xrRig; // XR Origin — assigner dans l'Inspector

    private NetworkVariable<int> playerMaterialIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private Vector3     _previousPosition;
    private const float AnimBlendSpeed   = 8f;
    private const float SprintMultiplier = 2f; // doit correspondre à la valeur desktop

    // ─── Spawn ────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (bodyRenderer    == null) Debug.LogError("[VRPlayerController] bodyRenderer non assigné !");
        if (animator        == null) Debug.LogWarning("[VRPlayerController] Animator non assigné — pas d'animation.");
        if (networkAnimator == null) Debug.LogWarning("[VRPlayerController] NetworkAnimator non assigné — pas de sync animation.");
        if (xrRig           == null) Debug.LogWarning("[VRPlayerController] xrRig non assigné !");
        if (playerMaterials == null || playerMaterials.Length == 0)
            Debug.LogError("[VRPlayerController] playerMaterials vide !");

        _previousPosition = xrRig != null ? xrRig.position : transform.position;

        if (IsOwner)
        {
            if (playerMaterials != null && playerMaterials.Length > 0)
                playerMaterialIndex.Value = (int)(OwnerClientId % (ulong)playerMaterials.Length);

            // On ne se voit pas soi-même en VR
            if (bodyRenderer != null)
                bodyRenderer.enabled = false;
        }

        playerMaterialIndex.OnValueChanged += OnMaterialIndexChanged;
        ApplyMaterial(playerMaterialIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        playerMaterialIndex.OnValueChanged -= OnMaterialIndexChanged;
    }

    // ─── Update — vélocité XR Rig → SpeedX / SpeedZ ──────────────────────────

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;
        if (animator == null || xrRig == null) return;

        // Vélocité world-space, plan horizontal uniquement (pas de Y)
        Vector3 worldVelocity = (xrRig.position - _previousPosition) / Time.deltaTime;
        worldVelocity.y       = 0f;
        _previousPosition     = xrRig.position;

        // Repère de la tête : yaw uniquement (comme transform.rotation en desktop)
        // On ignore le pitch/roll du casque pour ne pas fausser les blends
        Quaternion headYaw    = Quaternion.Euler(0f, xrRig.eulerAngles.y, 0f);
        Vector3 localVelocity = Quaternion.Inverse(headYaw) * worldVelocity;

        // Normalise par la vitesse de marche de base pour rester dans [-1, 1]
        // (multiplié par SprintMultiplier au sprint, comme en desktop)
        float speedRef = 3f; // doit correspondre à PlayerController.Speed
        float blendX   = Mathf.Clamp(localVelocity.x / speedRef, -SprintMultiplier, SprintMultiplier);
        float blendZ   = Mathf.Clamp(localVelocity.z / speedRef, -SprintMultiplier, SprintMultiplier);

        Debug.Log($"[VR Anim] blendX={blendX:F2} blendZ={blendZ:F2}");
        animator.SetFloat("SpeedX", blendX, 1f / AnimBlendSpeed, Time.deltaTime);
        animator.SetFloat("SpeedZ", blendZ, 1f / AnimBlendSpeed, Time.deltaTime);
    }

    // ─── Appearance ───────────────────────────────────────────────────────────

    private void OnMaterialIndexChanged(int oldIndex, int newIndex) => ApplyMaterial(newIndex);

    private void ApplyMaterial(int index)
    {
        if (bodyRenderer == null || playerMaterials == null || playerMaterials.Length == 0) return;
        if (index < 0 || index >= playerMaterials.Length) return;
        bodyRenderer.sharedMaterial = playerMaterials[index];
    }
}