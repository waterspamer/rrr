using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkVehicleEntity : MonoBehaviour
{
    [SerializeField] private string playerId;
    [SerializeField] private bool isLocalPlayer;

    public string PlayerId => playerId;
    public bool IsLocalPlayer => isLocalPlayer;

    public void Configure(string id, bool localPlayer)
    {
        playerId = id;
        isLocalPlayer = localPlayer;
    }
}
