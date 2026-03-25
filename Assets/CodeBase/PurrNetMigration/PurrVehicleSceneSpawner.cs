using System.Collections.Generic;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Prediction;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

[System.Serializable]
public struct PurrVehicleLoadoutMessage : IPackedAuto
{
    public string loadoutName;
    public int bodySetOptionIndex;
    public int engineIndex;
    public int suspensionIndex;
    public int paintIndex;

    public static PurrVehicleLoadoutMessage FromPayload(PlayerCarSelectionPayload payload)
    {
        return new PurrVehicleLoadoutMessage
        {
            loadoutName = payload != null ? payload.loadoutName ?? string.Empty : string.Empty,
            bodySetOptionIndex = payload != null ? payload.bodySetOptionIndex : -1,
            engineIndex = payload != null ? payload.engineIndex : -1,
            suspensionIndex = payload != null ? payload.suspensionIndex : -1,
            paintIndex = payload != null ? payload.paintIndex : -1
        };
    }

    public PlayerCarSelectionPayload ToPayload()
    {
        return new PlayerCarSelectionPayload
        {
            version = 1,
            loadoutName = loadoutName ?? string.Empty,
            bodySetOptionIndex = bodySetOptionIndex,
            engineIndex = engineIndex,
            suspensionIndex = suspensionIndex,
            paintIndex = paintIndex
        };
    }
}

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
    private readonly Dictionary<PlayerID, PlayerCarSelectionPayload> playerLoadouts = new Dictionary<PlayerID, PlayerCarSelectionPayload>();
    private readonly HashSet<PlayerID> queuedPlayers = new HashSet<PlayerID>();
    private readonly List<PlayerID> botPlayers = new List<PlayerID>();
    private readonly List<PlayerID> spawnBuffer = new List<PlayerID>();

    private NetworkManager activeManager;
    private ScenePlayersModule scenePlayers;
    private PlayersManager playersManager;
    private SceneID activeSceneId;
    private bool hasSceneId;
    private bool isServerSpawner;
    private bool isClientPublisher;
    private bool localLoadoutPublished;
    private bool isEnsuringBots;
    private int pendingBotCreates;
    private float nextWaitDiagnosticAt;

    public void Configure(PlayerCar template, int botCount, PredictionManager world)
    {
        templateCar = template;
        predictionManager = world;
        soloBotCount = Mathf.Max(0, botCount);
    }

    private void Update()
    {
        if (isClientPublisher)
            TryPublishLocalLoadout();

        if (isServerSpawner)
            TrySpawnQueuedPlayers();
    }

    public override void Subscribe(NetworkManager manager, bool asServer)
    {
        if (templateCar == null)
            return;

        activeManager = manager;
        ResolveReferences();
        if (playersManager != null)
            playersManager.Subscribe<PurrVehicleLoadoutMessage>(OnLoadoutMessage);

        if (!asServer)
        {
            isClientPublisher = true;
            Debug.Log($"PurrVehicleSceneSpawner: client subscribe scene='{gameObject.scene.name}' template='{templateCar.name}'", this);
            return;
        }

        isServerSpawner = true;
        Debug.Log($"PurrVehicleSceneSpawner: server subscribe scene='{gameObject.scene.name}' template='{templateCar.name}'", this);

        manager.onPlayerLoadedScene += OnPlayerLoadedScene;
        manager.onPlayerLeft += OnPlayerLeft;

        CacheSceneId(manager);
        QueueExistingPlayers();
        EnsureBots();
        TrySpawnQueuedPlayers();
    }

    public override void Unsubscribe(NetworkManager manager, bool asServer)
    {
        if (playersManager != null)
            playersManager.Unsubscribe<PurrVehicleLoadoutMessage>(OnLoadoutMessage);

        if (asServer)
        {
            manager.onPlayerLoadedScene -= OnPlayerLoadedScene;
            manager.onPlayerLeft -= OnPlayerLeft;
            isServerSpawner = false;
        }
        else
        {
            isClientPublisher = false;
            localLoadoutPublished = false;
        }

        activeManager = null;
    }

    private void ResolveReferences()
    {
        if (predictionManager == null)
            predictionManager = GetComponent<PredictionManager>();

        if (activeManager != null)
        {
            if (scenePlayers == null)
            {
                if (!activeManager.TryGetModule(out scenePlayers, true))
                    activeManager.TryGetModule(out scenePlayers, false);
            }
            if (playersManager == null)
            {
                if (!activeManager.TryGetModule(out playersManager, true))
                    activeManager.TryGetModule(out playersManager, false);
            }
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
        if (soloBotCount <= 0 || playersManager == null || scenePlayers == null || !hasSceneId || isEnsuringBots)
            return;

        isEnsuringBots = true;
        try
        {
            while (botPlayers.Count + pendingBotCreates < soloBotCount)
            {
                pendingBotCreates += 1;
                PlayerID bot;
                try
                {
                    bot = playersManager.CreateBot();
                }
                finally
                {
                    pendingBotCreates = Mathf.Max(0, pendingBotCreates - 1);
                }

                if (!botPlayers.Contains(bot))
                    botPlayers.Add(bot);

                if (!scenePlayers.IsPlayerLoadedInScene(bot, activeSceneId))
                    scenePlayers.AddPlayerToScene(bot, activeSceneId);

                QueueSpawn(bot);
                Debug.Log($"PurrVehicleSceneSpawner: created bot {bot} in scene {activeSceneId}.", this);
            }
        }
        finally
        {
            isEnsuringBots = false;
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
        }

        if (scene != activeSceneId)
            return;

        QueueSpawn(player);
        if (!player.isBot)
            EnsureBots();
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
        spawnBuffer.Sort(CompareSpawnPriority);

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

        int spawnSlot = ComputeSpawnSlot(player);
        Vector3 requestedSpawnPosition = templateCar.transform.position +
                                         templateCar.transform.right * (spawnSpacing * spawnSlot) +
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

        TryApplyLoadout(player, spawnedCar, instance);

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

    private void TryPublishLocalLoadout()
    {
        if (localLoadoutPublished || playersManager == null || !playersManager.localPlayerId.HasValue)
            return;

        if (!PlayerCarSelection.TryGetPayload(out PlayerCarSelectionPayload payload) || payload == null)
            return;

        PurrVehicleLoadoutMessage message = PurrVehicleLoadoutMessage.FromPayload(payload);
        playersManager.SendToServer(message, Channel.ReliableOrdered);
        localLoadoutPublished = true;
        Debug.Log(
            $"PurrVehicleSceneSpawner: published local loadout '{message.loadoutName}' for player {playersManager.localPlayerId.Value}.",
            this);
    }

    private void OnLoadoutMessage(PlayerID player, PurrVehicleLoadoutMessage message, bool asServer)
    {
        if (!asServer)
            return;

        PlayerCarSelectionPayload payload = message.ToPayload();
        playerLoadouts[player] = payload;
        Debug.Log($"PurrVehicleSceneSpawner: received loadout '{payload.loadoutName}' for player {player}.", this);

        if (!spawnedPlayers.TryGetValue(player, out PredictedObjectID objectId))
            return;

        if (!predictionManager.hierarchy.TryGetGameObject(objectId, out GameObject instance) || instance == null)
            return;

        if (!instance.TryGetComponent(out PlayerCar spawnedCar))
            return;

        if (TryApplyLoadout(player, spawnedCar, instance))
            RegroundSpawnedCar(spawnedCar, instance);
    }

    private bool TryApplyLoadout(PlayerID player, PlayerCar spawnedCar, GameObject instance)
    {
        if (spawnedCar == null || !playerLoadouts.TryGetValue(player, out PlayerCarSelectionPayload payload) || payload == null)
            return false;

        CarLoadoutConfig loadout = PlayerCarLoadoutUtility.ApplySelectedLoadout(spawnedCar, payload);
        Debug.Log(
            $"PurrVehicleSceneSpawner: applied loadout '{(loadout != null ? loadout.name : payload.loadoutName)}' to player {player}.",
            instance);
        return true;
    }

    private void RegroundSpawnedCar(PlayerCar spawnedCar, GameObject instance)
    {
        if (spawnedCar == null || instance == null)
            return;

        Vector3 groundedSpawnPosition = VehicleSpawnUtility.ResolveGroundedSpawnPosition(
            spawnedCar,
            instance.transform.position,
            spawnLift,
            groundProbeHeight,
            groundProbeDistance,
            instance.transform);
        instance.transform.SetPositionAndRotation(groundedSpawnPosition, instance.transform.rotation);
    }

    private int ComputeSpawnSlot(PlayerID player)
    {
        int humanCount = 0;
        int botCount = 0;
        foreach (PlayerID spawnedPlayer in spawnedPlayers.Keys)
        {
            if (spawnedPlayer.isBot)
                botCount += 1;
            else
                humanCount += 1;
        }

        return player.isBot ? humanCount + botCount : humanCount;
    }

    private static int CompareSpawnPriority(PlayerID a, PlayerID b)
    {
        if (a.isBot != b.isBot)
            return a.isBot ? 1 : -1;

        return a.id.value.CompareTo(b.id.value);
    }
}
