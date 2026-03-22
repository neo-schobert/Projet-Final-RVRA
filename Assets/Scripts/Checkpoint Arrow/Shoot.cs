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

    private NetworkVariable<int> _currentBullets = new NetworkVariable<int>(
        3,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private XRBaseInteractor _currentInteractor;
    private InputActionReference _activeAction;
    private bool _isHeld = false;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            _currentBullets.Value = maxBullets;

        _currentBullets.OnValueChanged += OnBulletsChanged;
        UpdateAmmoText(_currentBullets.Value);

        var grabbable = GetComponent<XRGrabInteractable>();
        grabbable.selectEntered.AddListener(OnGrabbed);
        grabbable.selectExited.AddListener(OnReleased);
    }

    public override void OnNetworkDespawn()
    {
        _currentBullets.OnValueChanged -= OnBulletsChanged;
        UnsubscribeAction();

        var grabbable = GetComponent<XRGrabInteractable>();
        if (grabbable != null)
        {
            grabbable.selectEntered.RemoveListener(OnGrabbed);
            grabbable.selectExited.RemoveListener(OnReleased);
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

    [ServerRpc(RequireOwnership = false)]
    private void RequestOwnershipServerRpc(ServerRpcParams rpcParams = default)
    {
        NetworkObject.ChangeOwnership(rpcParams.Receive.SenderClientId);
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
        SpawnBulletRpc(spawnPoint.position, spawnPoint.rotation);
        TriggerHaptic();
    }

    // En Distributed Authority, l'owner appelle ce Rpc directement
    // SendTo.Everyone = s'exécute sur tous les clients (y compris l'appelant)
    [Rpc(SendTo.Everyone)]
    private void SpawnBulletRpc(Vector3 spawnPos, Quaternion spawnRot)
    {
        GameObject bullet = Instantiate(bulletPrefab, spawnPos, spawnRot);
        var rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = spawnRot * Vector3.forward * bulletSpeed;
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
