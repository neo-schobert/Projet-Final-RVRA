using UnityEngine;
using Unity.Netcode;

public class NetworkedInteractionTool : NetworkBehaviour 
{
    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[NetworkedInteractionTool] Trigger avec : {other.gameObject.name}");
    }
}