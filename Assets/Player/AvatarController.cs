using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

[System.Serializable]
public class MapTransform
{
    public Transform vrTarget;
    public Transform IKTarget;
    public Vector3 trackingPositionOffset;
    public Vector3 trackingRotationOffset;

    public void MapVRAvatar()
    {
        IKTarget.position = vrTarget.TransformPoint(trackingPositionOffset);
        IKTarget.rotation = vrTarget.rotation * Quaternion.Euler(trackingRotationOffset);
    }
}

public class AvatarController : MonoBehaviour
{
    [SerializeField] private MapTransform head;
    [SerializeField] private MapTransform leftHand;
    [SerializeField] private MapTransform rightHand;
    [SerializeField] private float turnSmoothness;
    [SerializeField] private Transform IKHead;
    [SerializeField] private Vector3 headBodyOffset;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationSmoothness = 10f;

    private Vector3 _previousHeadPosition;
    private Vector2 _smoothedSpeed;
    private bool _isOwner;

    private void Start()
    {
        // Vérifie si ce joueur est local
        var netObj = GetComponentInParent<NetworkObject>();
        _isOwner = netObj != null && netObj.IsOwner;

        if (!_isOwner) return;

        AcquerirXROrigin();

        // FIX: Re-acquérir le XR Origin après chaque changement de scène.
        // Raison : Start() n'est exécuté qu'une fois (dans Scene.unity).
        // Après la transition vers TempleScene, l'XROrigin de Scene.unity est détruit
        // → IKHead devient null → l'avatar est figé.
        // sceneLoaded fire après Awake/Start de la nouvelle scène (XRRigSwitcher a déjà activé le bon rig).
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        // FIX: Se désabonner pour éviter les fuites mémoire si l'objet est détruit.
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // FIX: Callback déclenché après chaque chargement de scène.
    // Re-acquiert le XROrigin actif dans la nouvelle scène (activé par XRRigSwitcher.Awake).
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_isOwner) return;
        AcquerirXROrigin();
    }

    /// <summary>
    /// Trouve le XR Origin actif dans la scène courante et assigne les vrTargets.
    /// Appelé dans Start() et après chaque transition de scène (OnSceneLoaded).
    /// </summary>
    private void AcquerirXROrigin()
    {
        var xrOrigin = FindAnyObjectByType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogWarning("[AvatarController] XR Origin introuvable dans la scène !");
            return;
        }

        // Tête + IKHead → Main Camera du XR Origin
        head.vrTarget = xrOrigin.Camera.transform;
        IKHead        = xrOrigin.Camera.transform;

        // FIX: Réinitialiser les mains pour qu'elles soient re-cherchées dans la nouvelle scène.
        leftHand.vrTarget  = null;
        rightHand.vrTarget = null;

        // Controllers → cherche par nom dans les enfants du XR Origin
        foreach (Transform t in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLower();
            if (leftHand.vrTarget  == null && n.Contains("left")  && n.Contains("controller")) leftHand.vrTarget  = t;
            if (rightHand.vrTarget == null && n.Contains("right") && n.Contains("controller")) rightHand.vrTarget = t;
        }

        if (leftHand.vrTarget  == null) Debug.LogWarning("[AvatarController] Left Controller introuvable !");
        if (rightHand.vrTarget == null) Debug.LogWarning("[AvatarController] Right Controller introuvable !");

        _previousHeadPosition = IKHead.position;
        Debug.Log($"[AvatarController] XR Origin acquis : '{xrOrigin.name}' dans '{xrOrigin.gameObject.scene.name}'");
    }

    private void LateUpdate()
    {
        // Les joueurs distants ne font pas d'IK — animation reçue via NetworkAnimator
        if (!_isOwner) return;
        if (IKHead == null) return;

        // -- IK existant --
        transform.position = IKHead.position + headBodyOffset;
        transform.forward = Vector3.Lerp(
            transform.forward,
            Vector3.ProjectOnPlane(IKHead.forward, Vector3.up).normalized,
            Time.deltaTime * turnSmoothness
        );
        head.MapVRAvatar();
        leftHand.MapVRAvatar();
        rightHand.MapVRAvatar();

        // -- Animation --
        Vector3 worldVelocity = (IKHead.position - _previousHeadPosition) / Time.deltaTime;
        _previousHeadPosition = IKHead.position;

        Vector3 localVelocity = transform.InverseTransformDirection(worldVelocity);
        _smoothedSpeed = Vector2.Lerp(
            _smoothedSpeed,
            new Vector2(localVelocity.x, localVelocity.z),
            Time.deltaTime * animationSmoothness
        );
        animator.SetFloat("SpeedX", _smoothedSpeed.x);
        animator.SetFloat("SpeedZ", _smoothedSpeed.y);
    }
}
