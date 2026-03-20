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
    private float localRideHeight = 0.4f;
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
        Vector3 spawnPosition = ResolveSpawnPosition(local, player.SpawnPositionVector);
        root.position = spawnPosition;
        root.rotation = Quaternion.Euler(player.SpawnRotationVector);

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
        float rideHeight = MeasureLocalRideHeight(local);
        if (TryGetGroundHeight(requestedSpawnPosition, out float groundY))
            requestedSpawnPosition.y = groundY + rideHeight;
        else
            requestedSpawnPosition.y += rideHeight;

        return requestedSpawnPosition;
    }

    private float MeasureLocalRideHeight(PlayerCar local)
    {
        if (hasMeasuredLocalRideHeight)
            return localRideHeight;

        hasMeasuredLocalRideHeight = true;
        localRideHeight = Mathf.Max(0.05f, fallbackSpawnLift);
        if (local == null)
            return localRideHeight;

        Vector3 sampleOrigin = local.transform.position + Vector3.up * spawnGroundProbeHeight;
        if (Physics.Raycast(sampleOrigin, Vector3.down, out RaycastHit hit, spawnGroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            localRideHeight = Mathf.Max(0.05f, local.transform.position.y - hit.point.y);
            return localRideHeight;
        }

        return localRideHeight;
    }

    private bool TryGetGroundHeight(Vector3 aroundPosition, out float groundY)
    {
        Vector3 sampleOrigin = aroundPosition + Vector3.up * spawnGroundProbeHeight;
        if (Physics.Raycast(sampleOrigin, Vector3.down, out RaycastHit hit, spawnGroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }

        groundY = 0.0f;
        return false;
    }
}
