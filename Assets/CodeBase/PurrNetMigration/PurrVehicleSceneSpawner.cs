using System.Collections.Generic;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Prediction;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PurrVehicleSceneSpawner : PurrMonoBehaviour
{
    [SerializeField] private PlayerCar templateCar;
    [SerializeField] private PredictionManager predictionManager;
    [SerializeField, Range(0, 8)] private int soloBotCount;
    [SerializeField, Min(2.0f)] private float spawnSpacing = 7.0f;
    [SerializeField, Min(0.5f)] private float spawnLift = 1.5f;
    [SerializeField, Min(1.0f)] private float groundProbeHeight = 8.0f;
    [SerializeField, Min(1.0f)] private float groundProbeDistance = 20.0f;

    private readonly Dictionary<PlayerID, PredictedObjectID> spawnedPlayers = new Dictionary<PlayerID, PredictedObjectID>();
    private readonly HashSet<PlayerID> queuedPlayers = new HashSet<PlayerID>();
    private readonly List<PlayerID> botPlayers = new List<PlayerID>();
    private readonly List<PlayerID> spawnBuffer = new List<PlayerID>();

    private NetworkManager activeManager;
    private ScenePlayersModule scenePlayers;
    private PlayersManager playersManager;
    private SceneID activeSceneId;
    private bool hasSceneId;
    private bool isServerSpawner;
    private float nextWaitDiagnosticAt;

    public void Configure(PlayerCar template, int botCount, PredictionManager world)
    {
        templateCar = template;
        predictionManager = world;
        soloBotCount = Mathf.Max(0, botCount);
    }

    private void Update()
    {
        if (!isServerSpawner)
            return;

        TrySpawnQueuedPlayers();
    }

    public override void Subscribe(NetworkManager manager, bool asServer)
    {
        if (!asServer || templateCar == null)
            return;

        isServerSpawner = true;
        activeManager = manager;
        ResolveReferences();

        Debug.Log($"PurrVehicleSceneSpawner: subscribe scene='{gameObject.scene.name}' template='{templateCar.name}'", this);

        manager.onPlayerLoadedScene += OnPlayerLoadedScene;
        manager.onPlayerLeft += OnPlayerLeft;

        CacheSceneId(manager);
        QueueExistingPlayers();
        EnsureBots();
        TrySpawnQueuedPlayers();
    }

    public override void Unsubscribe(NetworkManager manager, bool asServer)
    {
        if (!asServer)
            return;

        manager.onPlayerLoadedScene -= OnPlayerLoadedScene;
        manager.onPlayerLeft -= OnPlayerLeft;
        isServerSpawner = false;
        activeManager = null;
    }

    private void ResolveReferences()
    {
        if (predictionManager == null)
            predictionManager = GetComponent<PredictionManager>();

        if (activeManager != null)
        {
            if (scenePlayers == null)
                activeManager.TryGetModule(out scenePlayers, true);
            if (playersManager == null)
                activeManager.TryGetModule(out playersManager, true);
        }
    }

    private void CacheSceneId(NetworkManager manager)
    {
        hasSceneId = manager != null && manager.TryGetSceneID(gameObject.scene, out activeSceneId);
        Debug.Log(
            hasSceneId
                ? $"PurrVehicleSceneSpawner: active scene id is {activeSceneId} for '{gameObject.scene.name}'."
                : $"PurrVehicleSceneSpawner: failed to resolve scene id for '{gameObject.scene.name}'.",
            this);
    }

    private void QueueExistingPlayers()
    {
        if (!hasSceneId || scenePlayers == null)
            return;

        if (!scenePlayers.TryGetPlayersInScene(activeSceneId, out IReadOnlyList<PlayerID> players) || players == null)
            return;

        for (int i = 0; i < players.Count; i++)
            QueueSpawn(players[i]);
    }

    private void EnsureBots()
    {
        if (soloBotCount <= 0 || playersManager == null || scenePlayers == null || !hasSceneId)
            return;

        while (botPlayers.Count < soloBotCount)
        {
            PlayerID bot = playersManager.CreateBot();
            botPlayers.Add(bot);
            scenePlayers.AddPlayerToScene(bot, activeSceneId);
            QueueSpawn(bot);
            Debug.Log($"PurrVehicleSceneSpawner: created bot {bot} in scene {activeSceneId}.", this);
        }
    }

    private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
    {
        if (!asServer)
            return;

        if (!hasSceneId)
        {
            activeSceneId = scene;
            hasSceneId = true;
            Debug.Log($"PurrVehicleSceneSpawner: late-bound active scene id to {activeSceneId}.", this);
            EnsureBots();
        }

        if (scene != activeSceneId)
            return;

        QueueSpawn(player);
        TrySpawnQueuedPlayers();
    }

    private void OnPlayerLeft(PlayerID player, bool asServer)
    {
        if (!asServer)
            return;

        queuedPlayers.Remove(player);

        if (!spawnedPlayers.Remove(player, out PredictedObjectID objectId))
            return;

        predictionManager?.hierarchy?.Delete(objectId);
    }

    private void QueueSpawn(PlayerID player)
    {
        if (spawnedPlayers.ContainsKey(player))
            return;

        queuedPlayers.Add(player);
    }

    private void TrySpawnQueuedPlayers()
    {
        ResolveReferences();

        if (!hasSceneId || queuedPlayers.Count == 0)
            return;

        if (predictionManager == null || predictionManager.hierarchy == null || scenePlayers == null || !predictionManager.isSpawned)
        {
            MaybeLogWaitReason();
            return;
        }

        spawnBuffer.Clear();
        foreach (PlayerID player in queuedPlayers)
            spawnBuffer.Add(player);

        for (int i = 0; i < spawnBuffer.Count; i++)
        {
            PlayerID player = spawnBuffer[i];
            if (TrySpawnPlayer(player))
                queuedPlayers.Remove(player);
        }
    }

    private bool TrySpawnPlayer(PlayerID player)
    {
        if (!hasSceneId || templateCar == null || spawnedPlayers.ContainsKey(player))
            return false;

        if (!scenePlayers.IsPlayerLoadedInScene(player, activeSceneId))
            return false;

        Vector3 requestedSpawnPosition = templateCar.transform.position +
                                         templateCar.transform.right * (spawnSpacing * spawnedPlayers.Count) +
                                         Vector3.up * spawnLift;
        Quaternion requestedSpawnRotation = templateCar.transform.rotation;

        if (!predictionManager.hierarchy.TryCreate(templateCar.gameObject, requestedSpawnPosition, requestedSpawnRotation, out PredictedObjectID objectId, player))
        {
            Debug.LogWarning($"PurrVehicleSceneSpawner: failed to create predicted vehicle for player {player}.", this);
            return false;
        }

        if (!predictionManager.hierarchy.TryGetGameObject(objectId, out GameObject instance) || instance == null)
        {
            Debug.LogWarning($"PurrVehicleSceneSpawner: hierarchy created player {player}, but runtime object was not found.", this);
            predictionManager.hierarchy.Delete(objectId);
            return false;
        }

        if (!instance.TryGetComponent(out PlayerCar spawnedCar))
        {
            Debug.LogWarning($"PurrVehicleSceneSpawner: spawned object for player {player} has no PlayerCar.", instance);
            predictionManager.hierarchy.Delete(objectId);
            return false;
        }

        Vector3 groundedSpawnPosition = VehicleSpawnUtility.ResolveGroundedSpawnPosition(
            spawnedCar,
            requestedSpawnPosition,
            spawnLift,
            groundProbeHeight,
            groundProbeDistance,
            instance.transform);
        instance.transform.SetPositionAndRotation(groundedSpawnPosition, requestedSpawnRotation);

        NetworkVehicleEntity entity = instance.GetComponent<NetworkVehicleEntity>();
        if (entity == null)
            entity = instance.AddComponent<NetworkVehicleEntity>();
        entity.Configure(player.ToString(), false);

        spawnedPlayers[player] = objectId;
        Debug.Log(
            $"PurrVehicleSceneSpawner: spawned predicted vehicle '{instance.name}' for player {player} at {groundedSpawnPosition} in scene {activeSceneId}.",
            instance);
        return true;
    }

    private void MaybeLogWaitReason()
    {
        if (Time.unscaledTime < nextWaitDiagnosticAt)
            return;

        nextWaitDiagnosticAt = Time.unscaledTime + 1.0f;
        Debug.LogWarning(
            $"PurrVehicleSceneSpawner: waiting for spawn. hasSceneId={hasSceneId} queued={queuedPlayers.Count} predictionManager={(predictionManager != null)} predictionSpawned={(predictionManager != null && predictionManager.isSpawned)} hierarchy={(predictionManager != null && predictionManager.hierarchy != null)} scenePlayers={(scenePlayers != null)}",
            this);
    }
}
