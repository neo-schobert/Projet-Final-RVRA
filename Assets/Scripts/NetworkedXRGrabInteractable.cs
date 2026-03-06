using UnityEngine;
using Unity.Netcode;

public class NetworkedXRGrabInteractable : NetworkBehaviour
{
    [SerializeField] protected NetworkObject networkObject;
    [SerializeField] protected Rigidbody rb;
    [SerializeField] protected Renderer colorRenderer;

    private Color catchableColor = Color.cyan;
    private Color caughtColor = Color.yellow;
    private Color initialColor;

    protected bool caught = false;

    public virtual new void Start()
    {
        initialColor = colorRenderer.material.color;
    }

    void Update() { }

    // -------------------------------------------------------------------------
    // Méthodes locales — appelées par les événements XRI sur le client local
    // -------------------------------------------------------------------------

    public virtual void LocalCatch()
    {
        print("LocalCatch");
        if (!caught)
        {
            if (!HasAuthority)
            {
                networkObject.RequestOwnership();
            }
            CatchRpc();
        }
    }

    public virtual void LocalRelease()
    {
        print("LocalRelease");
        ReleaseRpc();
    }

    public void LocalShowCatchable()
    {
        print("LocalShowCatchable");
        ShowCatchableRpc();
    }

    public void LocalHideCatchable()
    {
        print("LocalHideCatchable");
        HideCatchableRpc();
    }

    // -------------------------------------------------------------------------
    // RPC réseau — propagés à tous les clients
    // -------------------------------------------------------------------------

    [Rpc(SendTo.Everyone)]
    public virtual void CatchRpc()
    {
        print("CatchRpc");
        rb.isKinematic = true;
        caught = true;
        ShowCaughtRpc();
    }

    [Rpc(SendTo.Everyone)]
    public virtual void ReleaseRpc()
    {
        print("ReleaseRpc");
        rb.isKinematic = false;
        caught = false;
        ShowReleasedRpc();
    }

    [Rpc(SendTo.Everyone)]
    public void ShowCaughtRpc()
    {
        print("ShowCaughtRpc");
        colorRenderer.material.color = caughtColor;
    }

    [Rpc(SendTo.Everyone)]
    public void ShowReleasedRpc()
    {
        print("ShowReleasedRpc");
        colorRenderer.material.color = catchableColor;
    }

    [Rpc(SendTo.Everyone)]
    public void ShowCatchableRpc()
    {
        colorRenderer.material.color = catchableColor;
    }

    [Rpc(SendTo.Everyone)]
    public void HideCatchableRpc()
    {
        colorRenderer.material.color = initialColor;
    }
}