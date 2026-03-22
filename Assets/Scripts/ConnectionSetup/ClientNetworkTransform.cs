using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Version owner-authoritative de NetworkTransform.
/// Le client qui possède l'objet envoie les mises à jour de position/rotation/scale.
/// À utiliser à la place de NetworkTransform pour tous les objets contrôlés côté client (DA mode).
/// </summary>
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
