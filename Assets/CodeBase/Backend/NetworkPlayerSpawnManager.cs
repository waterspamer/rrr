using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkPlayerSpawnManager : MonoBehaviour
{
    [SerializeField] private PlayerCar localPlayerCar;
    [SerializeField] private Transform remotePlayersRoot;

    private bool localSpawnApplied;
    private readonly Dictionary<string, BackendMatchPlayerInfo> cachedPlayers = new Dictionary<string, BackendMatchPlayerInfo>(StringComparer.OrdinalIgnoreCase);

    public PlayerCar LocalPlayerCar
    {
        get
        {
            if (localPlayerCar == null)
                localPlayerCar = FindFirstObjectByType<PlayerCar>();
            return localPlayerCar;
        }
    }

    public Transform RemotePlayersRoot
    {
        get
        {
            if (remotePlayersRoot == null)
            {
                GameObject root = new GameObject("RemotePlayers");
                remotePlayersRoot = root.transform;
            }

            return remotePlayersRoot;
        }
    }

    public void CachePlayers(IReadOnlyList<BackendMatchPlayerInfo> players)
    {
        cachedPlayers.Clear();
        if (players == null)
            return;

        for (int i = 0; i < players.Count; i++)
        {
            BackendMatchPlayerInfo player = players[i];
            if (player == null || string.IsNullOrWhiteSpace(player.player_id))
                continue;

            cachedPlayers[player.player_id] = player;
        }
    }

    public BackendMatchPlayerInfo FindPlayer(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            return null;

        cachedPlayers.TryGetValue(playerId, out BackendMatchPlayerInfo player);
        return player;
    }

    public void ApplyLocalSpawn(string playerId, bool force = false)
    {
        if (localSpawnApplied && !force)
            return;

        BackendMatchPlayerInfo player = FindPlayer(playerId);
        if (player == null || !player.HasSpawnAssignment)
            return;

        PlayerCar local = LocalPlayerCar;
        if (local == null)
            return;

        Transform root = local.transform;
        root.position = player.SpawnPositionVector;
        root.rotation = Quaternion.Euler(player.SpawnRotationVector);

        Rigidbody body = local.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        localSpawnApplied = true;
    }
}
