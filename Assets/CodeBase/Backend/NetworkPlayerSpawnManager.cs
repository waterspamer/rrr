using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkPlayerSpawnManager : MonoBehaviour
{
    [SerializeField] private PlayerCar localPlayerCar;
    [SerializeField] private Transform remotePlayersRoot;
    [SerializeField, Min(0.0f)] private float fallbackSpawnLift = 0.4f;
    [SerializeField, Min(0.25f)] private float spawnGroundProbeHeight = 3.0f;
    [SerializeField, Min(0.5f)] private float spawnGroundProbeDistance = 8.0f;

    private bool localSpawnApplied;
    private bool hasMeasuredLocalRideHeight;
    private bool hasSpawnAnchor;
    private float localRideHeight = 0.4f;
    private Vector3 spawnAnchorPosition;
    private Quaternion spawnAnchorRotation = Quaternion.identity;
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

        EnsureSpawnAnchor(local.transform);
        Transform root = local.transform;
        Vector3 spawnPosition = ResolveSpawnPosition(local, player.SpawnPositionVector);
        root.position = spawnPosition;
        root.rotation = ResolveSpawnRotation(player.SpawnRotationVector);

        Rigidbody body = local.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        localSpawnApplied = true;
    }

    private Vector3 ResolveSpawnPosition(PlayerCar local, Vector3 requestedSpawnPosition)
    {
        requestedSpawnPosition = VehicleSpawnUtility.ResolveMatchSpawnPosition(
            requestedSpawnPosition,
            spawnAnchorPosition,
            spawnAnchorRotation);

        float rideHeight = MeasureLocalRideHeight(local);
        if (VehicleSpawnUtility.TryGetGroundHeight(
                requestedSpawnPosition,
                spawnGroundProbeHeight,
                spawnGroundProbeDistance,
                out float groundY))
            requestedSpawnPosition.y = groundY + rideHeight;
        else
            requestedSpawnPosition.y += rideHeight;

        return requestedSpawnPosition;
    }

    private Quaternion ResolveSpawnRotation(Vector3 requestedSpawnRotation)
    {
        return VehicleSpawnUtility.ResolveMatchSpawnRotation(requestedSpawnRotation, spawnAnchorRotation);
    }

    private float MeasureLocalRideHeight(PlayerCar local)
    {
        if (hasMeasuredLocalRideHeight)
            return localRideHeight;

        hasMeasuredLocalRideHeight = true;
        localRideHeight = VehicleSpawnUtility.MeasureRideHeight(local, fallbackSpawnLift);
        return localRideHeight;
    }

    private void EnsureSpawnAnchor(Transform root)
    {
        if (hasSpawnAnchor || root == null)
            return;

        hasSpawnAnchor = true;
        spawnAnchorPosition = root.position;
        spawnAnchorRotation = root.rotation;
    }
}
