using Unity.Netcode;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class Shoot : NetworkBehaviour
{
    [Header("Balle")]
    public GameObject bulletPrefab;
    public Transform spawnPoint;
    public float bulletSpeed = 10f;

    [Header("Munitions")]
    public int maxBullets = 10;
    public TextMeshPro ammoText;

    [Header("Input — assigne XRI Left/Right Hand Interaction/Activate")]
    public InputActionReference shootActionLeft;
    public InputActionReference shootActionRight;

    [Header("Haptique")]
    [Range(0f, 1f)] public float hapticAmplitude = 0.5f;
    public float hapticDuration = 0.1f;

    // ── Audio ─────────────────────────────────────────────────────────────────
    // Son joué localement sur chaque client via SpawnBulletRpc (SendTo.Everyone).
    // Pas besoin de RPC dédié : SpawnBulletRpc s'exécute déjà sur tous les clients,
    // donc chacun joue le son lui-même → pas de latence réseau supplémentaire.
    // Volume calculé selon la distance joueur local ↔ spawnPoint du tireur.
    // spatialBlend = 0 (2D) + volume manuel pour éviter la double atténuation
    // qu'on aurait en combinant rolloff Unity 3D et notre propre calcul.
    [Header("Audio - Tir")]
    [SerializeField] private AudioClip             _shootClip;
    [SerializeField] [Range(0f, 1f)] private float _shootVolume      = 1f;
    [SerializeField] private float                 _shootMinDistance  = 1f;  // en dessous → volume plein
    [SerializeField] private float                 _shootMaxDistance  = 20f; // au-delà → silence

    private NetworkVariable<int> _currentBullets = new NetworkVariable<int>(
        3,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private XRBaseInteractor   _currentInteractor;
    private InputActionReference _activeAction;
    private bool _isHeld = false;

    public override void OnNetworkSpawn()
    {
        // IsServer est TOUJOURS false en Distributed Authority (pas de serveur dédié).
        // IsOwner = le client qui a spawné / possède le gun → c'est lui qui initialise.
        if (IsOwner)
            _currentBullets.Value = maxBullets;

        _currentBullets.OnValueChanged += OnBulletsChanged;
        UpdateAmmoText(_currentBullets.Value);

        var grabbable = GetComponent<XRGrabInteractable>();
        grabbable.selectEntered.AddListener(OnGrabbed);
        grabbable.selectExited.AddListener(OnReleased);
    }

    public override void OnNetworkDespawn()
    {
        // Filet de sécurité : IsOwner est encore valide ici (pas encore dans OnDestroy).
        // Couvre le cas où XRI a forcé OnReleased avant ce callback (ordre non garanti).
        if (_isHeld && IsOwner)
        {
            GunPersistence.WasHeld = true;
            Debug.Log("[Shoot] OnNetworkDespawn — gun tenu, WasHeld sauvegardé (safety net).");
        }

        _currentBullets.OnValueChanged -= OnBulletsChanged;
        UnsubscribeAction();

        var grabbable = GetComponent<XRGrabInteractable>();
        if (grabbable != null)
        {
            grabbable.selectEntered.RemoveListener(OnGrabbed);
            grabbable.selectExited.RemoveListener(OnReleased);
        }
    }

    public override void OnDestroy()
    {
        // IsOwner n'est plus fiable ici (le NetworkObject est déjà despawné).
        // On vérifie uniquement _isHeld — dernier filet si ni OnReleased ni OnNetworkDespawn
        // n'ont pu sauvegarder (ex : objet non-networked ou ordre de destruction atypique).
        if (_isHeld)
        {
            GunPersistence.WasHeld = true;
            Debug.Log("[Shoot] OnDestroy — last resort, WasHeld=true sauvegardé.");
        }
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        _currentInteractor = args.interactorObject as XRBaseInteractor;
        _isHeld = true;

        Debug.Log($"[Shoot] OnGrabbed — IsOwner:{IsOwner} IsServer:{IsServer} interactor:{_currentInteractor?.gameObject.name}");

        // Transfert d'ownership si nécessaire
        if (IsSpawned && !IsOwner)
            RequestOwnershipServerRpc();

        // Détecte main gauche ou droite en remontant la hiérarchie
        // (le Near-Far Interactor s'appelle "Near-Far Interactor" des deux côtés,
        //  c'est son parent "Left Controller" / "Right Controller" qui porte le nom)
        bool isLeft = _currentInteractor != null && IsLeftHand(_currentInteractor.transform);

        // Mémorise l'état pour la persistance entre scènes
        GunPersistence.WasHeld      = true;
        GunPersistence.WasRightHand = !isLeft;

        _activeAction = isLeft ? shootActionLeft : shootActionRight;

        if (_activeAction == null)
        {
            Debug.LogError($"[Shoot] shootAction{(isLeft ? "Left" : "Right")} non assigné dans l'Inspector !");
            return;
        }

        Debug.Log($"[Shoot] Action trouvée : {_activeAction.action.name} | enabled:{_activeAction.action.enabled}");

        // On écoute 'started' (dès que le trigger passe le seuil) et non 'performed'
        // pour éviter le conflit avec l'état déjà pressed du grab
        _activeAction.action.started += OnShootInput;
        _activeAction.action.Enable();
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        _isHeld = false;
        _currentInteractor = null;

        // args.isCanceled = true  → XRI force le release (objet détruit, scène en transition).
        //                           Le joueur n'a PAS volontairement lâché le gun.
        //                           → NE PAS effacer WasHeld : GunAutoGrab doit re-graber dans TempleScene.
        //
        // args.isCanceled = false → Le joueur a ouvert la main volontairement.
        //                           → Effacer WasHeld : le gun ne sera PAS auto-grabé dans TempleScene.
        if (!args.isCanceled)
            GunPersistence.WasHeld = false;

        UnsubscribeAction();
    }

    private void OnShootInput(InputAction.CallbackContext ctx)
    {
        Debug.Log($"[Shoot] OnShootInput — _isHeld:{_isHeld} IsOwner:{IsOwner} bullets:{_currentBullets.Value}");
        if (!_isHeld) return;
        Fire();
    }

    private bool IsLeftHand(Transform t)
    {
        while (t != null)
        {
            string n = t.name.ToLower();
            if (n.Contains("left"))  return true;
            if (n.Contains("right")) return false;
            t = t.parent;
        }
        return false;
    }

    private void UnsubscribeAction()
    {
        if (_activeAction != null)
        {
            _activeAction.action.started -= OnShootInput;
            _activeAction = null;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestOwnershipServerRpc(RpcParams rpcParams = default)
    {
        NetworkObject.ChangeOwnership(rpcParams.Receive.SenderClientId);
    }

    /// <summary>
    /// Variante de Fire() sans vérification IsOwner.
    /// Utilisée par GunTempleSetup dans TempleScene où le gun est fixé dans la main
    /// et l'ownership NGO peut ne pas être encore transféré au bon client.
    /// </summary>
    public void FireLocal()
    {
        if (_currentBullets.Value <= 0) return;
        if (bulletPrefab == null) { Debug.LogError("[Shoot] bulletPrefab non assigné !"); return; }
        if (spawnPoint == null)   { Debug.LogError("[Shoot] spawnPoint non assigné !"); return; }

        if (IsOwner)
            _currentBullets.Value--;

        SpawnBulletRpc(spawnPoint.position, spawnPoint.rotation);
        TriggerHaptic();
    }

    public void Fire()
    {
        if (!IsOwner) return;
        if (_currentBullets.Value <= 0) return;
        if (bulletPrefab == null) { Debug.LogError("[Shoot] bulletPrefab non assigné !"); return; }
        if (spawnPoint == null)   { Debug.LogError("[Shoot] spawnPoint non assigné !"); return; }

        // Owner décrémente lui-même (WritePermission.Owner = seul l'owner peut écrire)
        _currentBullets.Value--;

        // DA mode : l'owner envoie directement à tous via SendTo.Everyone
        // Pas de ServerRpc intermédiaire — il n'y a pas de serveur en DA
        // On passe spawnPoint.position pour que chaque client calcule
        // la distance depuis ce point et ajuste le volume en conséquence.
        SpawnBulletRpc(spawnPoint.position, spawnPoint.rotation);
        TriggerHaptic();
    }

    // En Distributed Authority, l'owner appelle ce Rpc directement.
    // SendTo.Everyone = s'exécute sur tous les clients (y compris l'appelant).
    // Chaque client joue le son localement avec un volume calculé selon
    // sa propre distance au spawnPoint → pas de RPC audio séparé nécessaire.
    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    private void SpawnBulletRpc(Vector3 spawnPos, Quaternion spawnRot)
    {
        // ── Balle ─────────────────────────────────────────────────────────────
        GameObject bullet = Instantiate(bulletPrefab, spawnPos, spawnRot);
        var rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = spawnRot * Vector3.forward * bulletSpeed;

        // ── Son de tir ────────────────────────────────────────────────────────
        PlayShootAudio(spawnPos);
    }

    // ── Audio ─────────────────────────────────────────────────────────────────
    private void PlayShootAudio(Vector3 spawnPos)
    {
        if (_shootClip == null)
        {
            Debug.LogWarning("[Shoot] _shootClip est NULL — " +
                             "assigne un AudioClip de tir sur le gun dans l'Inspector !");
            return;
        }

        // Camera.main = caméra locale du client → distance correcte pour chaque joueur.
        Camera localCam        = Camera.main;
        float distanceToPlayer = localCam != null
            ? Vector3.Distance(spawnPos, localCam.transform.position)
            : 0f;

        // Atténuation SmoothStep : courbe en S, plus naturelle à l'oreille qu'un lerp brut.
        // En dessous de _shootMinDistance → volume plein.
        // Au-delà de _shootMaxDistance → silence total.
        float t = Mathf.Clamp01(
            (distanceToPlayer - _shootMinDistance) /
            (_shootMaxDistance - _shootMinDistance)
        );
        float volumeFinal = _shootVolume * (1f - Mathf.SmoothStep(0f, 1f, t));

        // PlayClipAtPoint crée un AudioSource temporaire qui se détruit seul.
        // On ne peut pas contrôler le spatialBlend via PlayClipAtPoint (forcé à 1),
        // donc on crée le GameObject manuellement pour garder spatialBlend=0 (2D pur).
        GameObject audioObj         = new GameObject("ShootAudio");
        audioObj.transform.position = spawnPos;

        AudioSource src  = audioObj.AddComponent<AudioSource>();
        src.clip         = _shootClip;
        src.spatialBlend = 0f;        // 2D : volume géré manuellement via volumeFinal
        src.volume       = volumeFinal;
        src.playOnAwake  = false;
        src.Play();

        Destroy(audioObj, _shootClip.length + 0.1f);
    }

    private void TriggerHaptic()
    {
        if (_currentInteractor == null) return;
        try
        {
            // XRIT 3.x : xrController est déprécié, on passe par l'interface IXRInteractor
            if (_currentInteractor is XRBaseInputInteractor inputInteractor)
            {
                var hapticImpulse = inputInteractor.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics.HapticImpulsePlayer>();
                if (hapticImpulse != null)
                    hapticImpulse.SendHapticImpulse(hapticAmplitude, hapticDuration);
            }
        }
        catch { /* haptic non critique, on ignore */ }
    }

    private void OnBulletsChanged(int oldVal, int newVal) => UpdateAmmoText(newVal);

    private void UpdateAmmoText(int value)
    {
        if (ammoText != null)
            ammoText.text = $"{value} / {maxBullets}";
    }
}